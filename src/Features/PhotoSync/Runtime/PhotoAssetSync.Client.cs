using Vintagestory.API.MathTools;
using Phototesting.PlateLifecycle.Rendering;
using Phototesting.PhotoSync.Contracts;
using Phototesting.PhotoSync.Storage;

namespace Phototesting.PhotoSync.Runtime
{
    // Client-side photo upload/download state, dedupe, and cache invalidation hooks.
    // Handles local file writes and block refresh when synced photos arrive.
    public sealed partial class PhotoAssetSync
    {
        // Accessed only from main thread (render callbacks and client event handlers).
        private readonly Dictionary<string, double> _clientRequestedAt = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        // client-side: in-progress download assemblies by photoId
        private readonly Dictionary<string, IncomingAssembly> _clientIncoming = new Dictionary<string, IncomingAssembly>(StringComparer.OrdinalIgnoreCase);
        private readonly object _clientIncomingLock = new object();

        // client-side: mounted blocks waiting for a specific photoId
        private readonly object _clientWaitLock = new object();
        private readonly Dictionary<string, HashSet<BlockPos>> _clientBlocksWaitingForPhoto = new Dictionary<string, HashSet<BlockPos>>(StringComparer.OrdinalIgnoreCase);

        private long _clientLastStateCleanupMs;

        // Starts the upload path after a local capture has successfully written its png to disk.
        public void ClientOnPhotoCreated(string photoId)
        {
            if (_mod.ClientApi == null || _mod.ClientChannel == null) return;

            // Upload to server (best effort).
            string path = PhotoAssetStoragePaths.GetPhotoPath(photoId);
            TryUploadPhoto(photoId, path);
        }

        // Requests a missing photo from the server with short-term dedupe so repeated render checks do not spam packets.
        public void ClientRequestPhotoIfMissing(string photoId)
        {
            if (_mod.ClientApi == null || _mod.ClientChannel == null) return;

            if (!TryNormalizePhotoId(photoId, out string normalizedPhotoId)) return;

            string path = PhotoAssetStoragePaths.GetPhotoPath(normalizedPhotoId);
            if (File.Exists(path)) return;

            // Use a monotonic, process-wide clock so reconnecting (new World instance) doesn't break dedupe.
            long nowMs = Environment.TickCount64;
            double now = nowMs / 1000.0;
            ClientMaybeCleanupState(nowMs, now);

            if (_clientRequestedAt.TryGetValue(normalizedPhotoId, out double lastAt) && (now - lastAt) < 2.0)
            {
                return;
            }

            _clientRequestedAt[normalizedPhotoId] = now;
            _mod.ClientChannel.SendPacket(new PhotoBlobRequestPacket { PhotoId = normalizedPhotoId });
        }

        // Records that a mounted-photo block is waiting on this photo so it can be marked dirty when the download completes.
        public void ClientNoteBlockWaitingForPhoto(string photoId, BlockPos pos)
        {
            if (_mod.ClientApi == null) return;

            if (!TryNormalizePhotoId(photoId, out string normalizedPhotoId)) return;

            // Copy to avoid retaining a mutable BlockPos reference.
            BlockPos keyPos = new BlockPos(pos.X, pos.Y, pos.Z);

            lock (_clientWaitLock)
            {
                if (!_clientBlocksWaitingForPhoto.TryGetValue(normalizedPhotoId, out HashSet<BlockPos>? set) || set == null)
                {
                    set = new HashSet<BlockPos>();
                    _clientBlocksWaitingForPhoto[normalizedPhotoId] = set;
                }

                set.Add(keyPos);
            }
        }


        // Reads the just-captured local png and uploads it to the server if it fits the configured transfer limits.
        private void TryUploadPhoto(string photoId, string path)
        {
            if (_mod.ClientApi == null || _mod.ClientChannel == null) return;

            if (!TryNormalizePhotoId(photoId, out string normalizedPhotoId)) return;

            if (!File.Exists(path))
            {
                // Nothing to upload.
                return;
            }

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch
            {
                return;
            }

            if (bytes.Length <= 0 || bytes.Length > GetMaxTransferBytes())
            {
                Log.Warn(_mod.ClientApi.Logger, $"not uploading photo {normalizedPhotoId} (size {bytes.Length} bytes exceeds limit)");
                return;
            }

            SendChunksConfigured(_mod.ClientChannel, normalizedPhotoId, bytes, isUpload: true);
        }

        // Reassembles incoming download chunks, writes the finished file to disk, and invalidates waiting client renders.
        public void ClientHandleChunk(PhotoBlobChunkPacket packet)
        {
            if (_mod.ClientApi == null) return;
            if (packet == null) return;
            if (packet.IsUpload) return; // ignore uploads on client

            long nowMs = Environment.TickCount64;
            ClientMaybeCleanupState(nowMs, nowMs / 1000.0);

            if (!TryNormalizeAndValidateChunkPacket(packet, out string photoId)) return;

            if (!TryProcessIncomingChunk(_clientIncoming, _clientIncomingLock, photoId, packet, nowMs, out IncomingAssembly? completed)
                || completed == null)
            {
                return;
            }

            // Basic PNG signature check
            if (!LooksLikePng(completed.Buffer, completed.TotalSize))
            {
                Log.Warn(_mod.ClientApi.Logger, $"downloaded bytes for {photoId} do not look like PNG; ignoring");
                return;
            }

            if (!TryWritePhotoBytes(photoId, completed.Buffer, writeLock: null, out string? error))
            {
                Log.Warn(_mod.ClientApi.Logger, $"failed writing downloaded photo {photoId}: {error ?? "Unknown write error"}");
                return;
            }

            // Kick plate render cache so the next render pulls from disk.
            // (After framed-display removal in prep for the kos-pm merge there is no separate per-item photo cache;
            // the new frame BE is expected to register its own invalidation if it adds caching.)
            PhotoPlateRenderUtil.ClearClientRenderCacheAndBumpVersion();

            // Nudge any mounted-photo blocks that were waiting on this file.
            ClientMarkWaitingBlocksDirty(photoId);
        }

        // Periodically prunes stale request dedupe and abandoned incoming download assemblies.
        private void ClientMaybeCleanupState(long nowMs, double nowSeconds)
        {
            long cleanupIntervalMs = SyncCfg?.ClientStateCleanupIntervalMs ?? 15_000;
            float requestRetainSeconds = SyncCfg?.ClientRequestRetainSeconds ?? 300f;
            long incomingStaleMs = SyncCfg?.ClientIncomingStaleMs ?? 120_000;

            if (nowMs - _clientLastStateCleanupMs < cleanupIntervalMs) return;
            _clientLastStateCleanupMs = nowMs;

            if (_clientRequestedAt.Count > 0)
            {
                List<string>? staleRequestKeys = null;
                foreach (KeyValuePair<string, double> kvp in _clientRequestedAt)
                {
                    if (nowSeconds - kvp.Value <= requestRetainSeconds) continue;
                    staleRequestKeys ??= new List<string>();
                    staleRequestKeys.Add(kvp.Key);
                }

                if (staleRequestKeys != null)
                {
                    foreach (string key in staleRequestKeys)
                    {
                        _clientRequestedAt.Remove(key);
                    }
                }
            }

            PruneStaleIncomingAssemblies(_clientIncoming, _clientIncomingLock, nowMs, incomingStaleMs);
        }

        // Marks all mounted-photo blocks waiting on this photo as dirty so their meshes refresh from the newly written file.
        private void ClientMarkWaitingBlocksDirty(string photoId)
        {
            if (_mod.ClientApi == null) return;

            List<BlockPos>? positions = null;
            lock (_clientWaitLock)
            {
                if (_clientBlocksWaitingForPhoto.TryGetValue(photoId, out HashSet<BlockPos>? set) && set != null && set.Count > 0)
                {
                    positions = new List<BlockPos>(set);
                    _clientBlocksWaitingForPhoto.Remove(photoId);
                }
            }

            if (positions == null) return;

            _mod.ClientApi.Event.EnqueueMainThreadTask(() =>
            {
                foreach (BlockPos p in positions)
                {
                    try
                    {
                        _mod.ClientApi.World.BlockAccessor.MarkBlockDirty(p);
                    }
                    catch { /* intentional: best-effort non-critical path */ }
                }
            }, "phototesting-photo-arrived-markdirty");
        }

        // Logs failed transfer acknowledgements while keeping successful sync quiet.
        public void ClientHandleAck(PhotoBlobAckPacket packet)
        {
            // Keep quiet unless failure.
            if (_mod.ClientApi == null) return;
            if (packet == null) return;

            if (!packet.Ok)
            {
                Log.Warn(_mod.ClientApi.Logger, $"photo transfer ack failed for {packet.PhotoId}: {packet.Error}");
            }
        }
    }
}

