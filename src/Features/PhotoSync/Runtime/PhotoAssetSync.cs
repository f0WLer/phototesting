using Vintagestory.API.Client;
using Vintagestory.API.Server;
using Phototesting.AdminTooling;
using Phototesting.PhotoSync.Contracts;
using Phototesting.PhotoSync.Storage;

namespace Phototesting.PhotoSync.Runtime
{
    // Shared chunk-transfer primitives and photo-id/path normalization for sync.
    // Used by both client and server photo-sync partials.
    public sealed partial class PhotoAssetSync
    {
        private const int DefaultChunkSize = 24 * 1024;
        private const int DefaultMaxBytes = 2 * 1024 * 1024; // plenty for configured capture sizes

        private readonly PhotoTestingModSystem _mod;

        private sealed class IncomingAssembly
        {
            // Declared transfer envelope from the first accepted chunk.
            public readonly int TotalSize;
            public readonly int ChunkCount;

            // Shared destination buffer for all chunks in this transfer.
            public readonly byte[] Buffer;

            // Sparse-receive bookkeeping so out-of-order chunks and duplicates are handled safely.
            public int ReceivedChunks;
            public readonly bool[] Received;

            // Monotonic timestamp used by stale-assembly pruning.
            public long LastTouchedMs;

            public IncomingAssembly(int totalSize, int chunkCount)
            {
                TotalSize = totalSize;
                ChunkCount = chunkCount;
                Buffer = new byte[totalSize];
                Received = new bool[chunkCount];
                ReceivedChunks = 0;
                LastTouchedMs = Environment.TickCount64;
            }
        }

        public PhotoAssetSync(PhotoTestingModSystem mod)
        {
            this._mod = mod;
        }

        private PhotoSyncConfig? SyncCfg => _mod?.Config?.PhotoSync;

        // -------------------- Server-side throttles & upload authorization --------------------

        // Single in-mod tuning point. The only knob exposed to admin config is
        // ServerMaxOpenUploadsPerPlayer (the value most likely to need server-specific tuning).
        private const int RequestPermitsPerMinute = 60;
        private const int RequestBurstCapacity = 8;
        private const long ExpectedUploadTtlMs = 60_000;

        private PlayerNetworkThrottle? _serverRequestThrottle;
        private ServerExpectedUploads? _serverExpectedUploads;

        internal PlayerNetworkThrottle ServerRequestThrottle
            => _serverRequestThrottle ??= new PlayerNetworkThrottle(RequestPermitsPerMinute, RequestBurstCapacity);

        internal ServerExpectedUploads ExpectedUploads
            => _serverExpectedUploads ??= new ServerExpectedUploads(ExpectedUploadTtlMs);

        // Called by the camera authority after a PhotoTakenPacket has been authoritatively accepted.
        // Marks this (player, photoId) pair as eligible to upload bytes within the configured TTL.
        public void RegisterExpectedUpload(string playerUid, string photoId)
        {
            ExpectedUploads.Register(playerUid, photoId, Environment.TickCount64);
        }

        // Gets chunk size.
        private int GetChunkSizeBytes()
        {
            int size = SyncCfg?.ChunkSizeBytes ?? DefaultChunkSize;
            if (size < 1024) size = 1024;
            return size;
        }

        // Reads the maximum allowed upload/download size so sync logic can reject oversized transfers early.
        private int GetMaxTransferBytes()
        {
            int max = SyncCfg?.MaxTransferBytes ?? DefaultMaxBytes;
            if (max < 16 * 1024) max = 16 * 1024;
            return max;
        }

        // -------------------- Chunk helpers --------------------

        // Sends all chunks through the configured client channel using the current chunk-size setting.
        private void SendChunksConfigured(IClientNetworkChannel channel, string photoId, byte[] bytes, bool isUpload)
        {
            SendChunksCommon(photoId, bytes, isUpload, GetChunkSizeBytes(), pkt => channel.SendPacket(pkt));
        }

        // Sends all chunks through the configured server channel to a specific player using the current chunk-size setting.
        private void SendChunksConfigured(IServerNetworkChannel channel, IServerPlayer toPlayer, string photoId, byte[] bytes, bool isUpload)
        {
            SendChunksCommon(photoId, bytes, isUpload, GetChunkSizeBytes(), pkt => channel.SendPacket(pkt, toPlayer));
        }

        // Splits a byte buffer into ordered chunk packets for either uploads or downloads.
        private static void SendChunksCommon(string photoId, byte[] bytes, bool isUpload, int chunkSizeBytes, Action<PhotoBlobChunkPacket> send)
        {
            int chunkSize = Math.Max(1024, chunkSizeBytes);
            int chunkCount = (bytes.Length + chunkSize - 1) / chunkSize;
            for (int i = 0; i < chunkCount; i++)
            {
                int offset = i * chunkSize;
                int len = Math.Min(chunkSize, bytes.Length - offset);
                byte[] chunk = new byte[len];
                Buffer.BlockCopy(bytes, offset, chunk, 0, len);

                send(new PhotoBlobChunkPacket
                {
                    PhotoId = photoId,
                    TotalSize = bytes.Length,
                    ChunkIndex = i,
                    ChunkCount = chunkCount,
                    Data = chunk,
                    IsUpload = isUpload,
                    ChunkOffset = offset
                });
            }
        }

        // Validates the transfer envelope of one chunk packet before any assembly or copy work starts.
        private static bool IsChunkPacketShapeValid(PhotoBlobChunkPacket packet, int maxTransferBytes)
        {
            if (packet == null) return false;
            if (packet.TotalSize <= 0 || packet.TotalSize > maxTransferBytes) return false;
            if (packet.ChunkCount <= 0 || packet.ChunkCount > 4096) return false;
            if (packet.ChunkIndex < 0 || packet.ChunkIndex >= packet.ChunkCount) return false;
            if (packet.Data == null || packet.Data.Length <= 0) return false;
            if (packet.ChunkOffset < 0 || packet.ChunkOffset >= packet.TotalSize) return false;

            // Enforce strict offset/index shape so old packets without ChunkOffset are rejected.
            if (packet.ChunkIndex == 0)
            {
                if (packet.ChunkOffset != 0) return false;
            }
            else if (packet.ChunkOffset < packet.ChunkIndex)
            {
                return false;
            }

            if (packet.Data.Length > packet.TotalSize - packet.ChunkOffset) return false;
            return true;
        }

        // Canonical photo-id normalization gate shared by client/server sync flows.
        private static bool TryNormalizePhotoId(string rawPhotoId, out string photoId)
        {
            photoId = PhotoAssetStoragePaths.NormalizePhotoId(rawPhotoId);
            return !string.IsNullOrEmpty(photoId);
        }

        // Canonical chunk packet gate shared by client/server handlers.
        private bool TryNormalizeAndValidateChunkPacket(PhotoBlobChunkPacket packet, out string photoId)
        {
            if (!TryNormalizePhotoId(packet.PhotoId, out photoId)) return false;

            return IsChunkPacketShapeValid(packet, GetMaxTransferBytes());
        }

        // Applies one validated chunk into an in-progress assembly.
        private static bool TryApplyChunkToAssembly(IncomingAssembly asm, PhotoBlobChunkPacket packet)
        {
            if (asm.Received[packet.ChunkIndex]) return false;
            if (packet.ChunkOffset + packet.Data.Length > asm.TotalSize) return false;

            Buffer.BlockCopy(packet.Data, 0, asm.Buffer, packet.ChunkOffset, packet.Data.Length);
            asm.Received[packet.ChunkIndex] = true;
            asm.ReceivedChunks++;
            return true;
        }

        // Resolves or creates one incoming assembly, applies the chunk, and returns a completed buffer when the transfer finishes.
        private static bool TryProcessIncomingChunk(
            Dictionary<string, IncomingAssembly> incomingByKey,
            object incomingLock,
            string assemblyKey,
            PhotoBlobChunkPacket packet,
            long nowMs,
            out IncomingAssembly? completed)
        {
            completed = null;

            lock (incomingLock)
            {
                if (!incomingByKey.TryGetValue(assemblyKey, out IncomingAssembly? asm)
                    || asm == null
                    || asm.TotalSize != packet.TotalSize
                    || asm.ChunkCount != packet.ChunkCount)
                {
                    asm = new IncomingAssembly(packet.TotalSize, packet.ChunkCount);
                    incomingByKey[assemblyKey] = asm;
                }

                asm.LastTouchedMs = nowMs;

                if (!TryApplyChunkToAssembly(asm, packet)) return false;
                if (asm.ReceivedChunks < asm.ChunkCount) return false;

                incomingByKey.Remove(assemblyKey);
                completed = asm;
                return true;
            }
        }

        // Removes stale incoming assemblies older than the configured age budget.
        private static void PruneStaleIncomingAssemblies(
            Dictionary<string, IncomingAssembly> incomingByKey,
            object incomingLock,
            long nowMs,
            long staleAfterMs)
        {
            lock (incomingLock)
            {
                if (incomingByKey.Count == 0) return;

                List<string>? staleKeys = null;
                foreach (KeyValuePair<string, IncomingAssembly> kvp in incomingByKey)
                {
                    IncomingAssembly asm = kvp.Value;
                    if (asm == null) continue;
                    if (nowMs - asm.LastTouchedMs <= staleAfterMs) continue;

                    staleKeys ??= new List<string>();
                    staleKeys.Add(kvp.Key);
                }

                if (staleKeys == null) return;
                foreach (string key in staleKeys)
                {
                    incomingByKey.Remove(key);
                }
            }
        }

        // Persists completed photo bytes to canonical storage, optionally under a caller-supplied write lock.
        private static bool TryWritePhotoBytes(string photoId, byte[] bytes, object? writeLock, out string? error)
        {
            error = null;

            try
            {
                string outPath = PhotoAssetStoragePaths.GetPhotoPath(photoId);
                Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

                if (writeLock == null)
                {
                    File.WriteAllBytes(outPath, bytes);
                }
                else
                {
                    lock (writeLock)
                    {
                        File.WriteAllBytes(outPath, bytes);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        // Fast signature gate used before writing assembled bytes to disk.
        private static bool LooksLikePng(byte[] buffer, int totalSize)
        {
            return totalSize >= 8
                && buffer.Length >= 8
                && buffer[0] == 0x89
                && buffer[1] == 0x50
                && buffer[2] == 0x4E
                && buffer[3] == 0x47;
        }
    }
}
