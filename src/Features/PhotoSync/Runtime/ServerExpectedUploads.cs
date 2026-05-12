namespace Phototesting.PhotoSync.Runtime
{
    // Tracks expected photo uploads per (playerUid, photoId) so the upload chunk handler
    // can reject blob uploads for photo ids the player never legitimately captured.
    // Entries are registered when the server accepts a PhotoTakenPacket and consumed
    // (single-use) when the matching upload completes. Stale entries expire by TTL.
    internal sealed class ServerExpectedUploads
    {
        private readonly object _lock = new();
        private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

        // Per-player open-upload counts, capped to enforce concurrent-upload limit.
        private readonly Dictionary<string, int> _openByPlayer = new(StringComparer.Ordinal);

        private long _lastPruneMs;
        private readonly long _ttlMs;
        private const long PruneIntervalMs = 30_000;

        public ServerExpectedUploads(long ttlMs)
        {
            _ttlMs = Math.Max(5_000L, ttlMs);
        }

        // Registers an expected upload after the server has authoritatively accepted PhotoTakenPacket.
        public void Register(string playerUid, string photoId, long nowMs)
        {
            if (string.IsNullOrEmpty(playerUid) || string.IsNullOrEmpty(photoId)) return;

            string key = MakeKey(playerUid, photoId);
            lock (_lock)
            {
                MaybePrune_NoLock(nowMs);
                _entries[key] = new Entry { ExpiresAtMs = nowMs + _ttlMs };
            }
        }

        // Returns true if the (player, photoId) is in the expected set. Does not consume.
        public bool IsExpected(string playerUid, string photoId, long nowMs)
        {
            if (string.IsNullOrEmpty(playerUid) || string.IsNullOrEmpty(photoId)) return false;

            string key = MakeKey(playerUid, photoId);
            lock (_lock)
            {
                if (!_entries.TryGetValue(key, out Entry e)) return false;
                if (nowMs > e.ExpiresAtMs)
                {
                    _entries.Remove(key);
                    return false;
                }
                return true;
            }
        }

        // Removes the entry once the upload completes successfully.
        public void Consume(string playerUid, string photoId)
        {
            if (string.IsNullOrEmpty(playerUid) || string.IsNullOrEmpty(photoId)) return;
            string key = MakeKey(playerUid, photoId);
            lock (_lock) { _entries.Remove(key); }
        }

        // Increments the per-player open-upload count if it would not exceed the cap. Returns success.
        public bool TryBeginUpload(string playerUid, int maxOpenPerPlayer)
        {
            if (string.IsNullOrEmpty(playerUid)) return false;
            if (maxOpenPerPlayer < 1) maxOpenPerPlayer = 1;

            lock (_lock)
            {
                _openByPlayer.TryGetValue(playerUid, out int count);
                if (count >= maxOpenPerPlayer) return false;
                _openByPlayer[playerUid] = count + 1;
                return true;
            }
        }

        // Decrements the per-player open-upload count when an upload finishes or is abandoned.
        public void EndUpload(string playerUid)
        {
            if (string.IsNullOrEmpty(playerUid)) return;
            lock (_lock)
            {
                if (!_openByPlayer.TryGetValue(playerUid, out int count)) return;
                count--;
                if (count <= 0) _openByPlayer.Remove(playerUid);
                else _openByPlayer[playerUid] = count;
            }
        }

        private void MaybePrune_NoLock(long nowMs)
        {
            if (_lastPruneMs != 0 && (nowMs - _lastPruneMs) < PruneIntervalMs) return;
            _lastPruneMs = nowMs;

            if (_entries.Count == 0) return;

            List<string>? stale = null;
            foreach (KeyValuePair<string, Entry> kvp in _entries)
            {
                if (nowMs > kvp.Value.ExpiresAtMs)
                {
                    stale ??= new List<string>();
                    stale.Add(kvp.Key);
                }
            }

            if (stale == null) return;
            foreach (string k in stale) _entries.Remove(k);
        }

        private static string MakeKey(string playerUid, string photoId) => playerUid + "|" + photoId;

        private struct Entry
        {
            public long ExpiresAtMs;
        }
    }
}
