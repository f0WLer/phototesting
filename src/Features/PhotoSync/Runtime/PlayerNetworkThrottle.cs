namespace Phototesting.PhotoSync.Runtime
{
    // Per-player token-bucket rate limiter for server packet handlers.
    // Allocates one bucket per (key, playerUid). Caller decides the key (e.g. "seen", "request").
    // Idle entries are pruned by the next TryConsume call after the prune interval elapses.
    internal sealed class PlayerNetworkThrottle
    {
        private readonly object _lock = new();
        private readonly Dictionary<string, Bucket> _buckets = new(StringComparer.Ordinal);
        private long _lastPruneMs;

        private const long PruneIntervalMs = 60_000;
        private const long IdleEvictMs = 5 * 60_000;

        private readonly double _refillTokensPerMs;
        private readonly double _capacityTokens;

        public PlayerNetworkThrottle(int permitsPerMinute, int burstCapacity)
        {
            if (permitsPerMinute < 1) permitsPerMinute = 1;
            if (burstCapacity < 1) burstCapacity = 1;
            _refillTokensPerMs = permitsPerMinute / 60_000.0;
            _capacityTokens = burstCapacity;
        }

        // Attempts to consume one permit for the given player+scope key.
        // Returns true if the request is within budget, false if it should be dropped.
        public bool TryConsume(string playerUid, string scopeKey, long nowMs)
        {
            if (string.IsNullOrEmpty(playerUid)) return false;

            string key = scopeKey + "|" + playerUid;

            lock (_lock)
            {
                MaybePrune(nowMs);

                if (!_buckets.TryGetValue(key, out Bucket b))
                {
                    b = new Bucket { Tokens = _capacityTokens, LastRefillMs = nowMs };
                }

                long elapsed = nowMs - b.LastRefillMs;
                if (elapsed > 0)
                {
                    b.Tokens = Math.Min(_capacityTokens, b.Tokens + elapsed * _refillTokensPerMs);
                    b.LastRefillMs = nowMs;
                }

                if (b.Tokens < 1.0)
                {
                    _buckets[key] = b;
                    return false;
                }

                b.Tokens -= 1.0;
                _buckets[key] = b;
                return true;
            }
        }

        private void MaybePrune(long nowMs)
        {
            if (_lastPruneMs != 0 && (nowMs - _lastPruneMs) < PruneIntervalMs) return;
            _lastPruneMs = nowMs;

            if (_buckets.Count == 0) return;

            List<string>? stale = null;
            foreach (KeyValuePair<string, Bucket> kvp in _buckets)
            {
                if (nowMs - kvp.Value.LastRefillMs > IdleEvictMs)
                {
                    stale ??= new List<string>();
                    stale.Add(kvp.Key);
                }
            }

            if (stale == null) return;
            foreach (string k in stale) _buckets.Remove(k);
        }

        private struct Bucket
        {
            public double Tokens;
            public long LastRefillMs;
        }
    }
}
