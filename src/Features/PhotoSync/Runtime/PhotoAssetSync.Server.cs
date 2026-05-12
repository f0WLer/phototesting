using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Phototesting.PhotoSync.Contracts;
using Phototesting.PhotoSync.Storage;

namespace Phototesting.PhotoSync.Runtime
{
    // Server-side photo request/upload handling and incoming-assembly cleanup.
    // Reassembles chunks, validates png payloads, and persists synced photo bytes.
    public sealed partial class PhotoAssetSync
    {
        // server-side: in-progress uploads by (playerUid|photoId)
        private readonly Dictionary<string, IncomingAssembly> _serverIncoming = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _serverIncomingLock = new();
        private readonly object _writeLock = new();

        // Minimal cleanup so abandoned uploads (disconnects mid-transfer) don't accumulate.
        private long _serverLastPruneMs;

        // Small server tick hook that lets the mod system periodically prune abandoned upload assemblies.
        public void ServerPruneTick(long nowMs) => ServerMaybePruneIncoming(nowMs);

        // Sends one normalized transfer acknowledgement back to the requesting player.
        private void SendServerTransferAck(IServerPlayer toPlayer, string photoId, bool ok, string? error = null)
        {
            if (_mod.ServerChannel == null) return;

            _mod.ServerChannel.SendPacket(new PhotoBlobAckPacket
            {
                PhotoId = photoId,
                Ok = ok,
                Error = ok ? string.Empty : (error ?? "Photo transfer failed")
            }, toPlayer);
        }

        // Loads one persisted photo for download and validates transfer-size constraints.
        private bool TryLoadPhotoBytesForDownload(string photoId, out byte[]? bytes, out string? error)
        {
            bytes = null;
            error = null;

            string path = PhotoAssetStoragePaths.GetPhotoPath(photoId);
            if (!File.Exists(path))
            {
                error = "Photo not present on server";
                return false;
            }

            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }

            if (bytes.Length <= 0 || bytes.Length > GetMaxTransferBytes())
            {
                bytes = null;
                error = "Photo too large";
                return false;
            }

            return true;
        }

        // Applies server upload completion policy (png gate, disk write, seen touch, ack response).
        // The png-gate runs on the calling thread; the actual file write is dispatched to the thread pool
        // so it does not block the packet handler / main server thread.
        private void TryPersistUploadedPhoto(IServerPlayer fromPlayer, string photoId, IncomingAssembly completed)
        {
            if (!LooksLikePng(completed.Buffer, completed.TotalSize))
            {
                SendServerTransferAck(fromPlayer, photoId, ok: false, "Invalid PNG");
                return;
            }

            byte[] buffer = completed.Buffer;
            object writeLock = _writeLock;
            var sapi = _mod.Api as ICoreServerAPI;

            TyronThreadPool.QueueTask(() =>
            {
                bool ok = TryWritePhotoBytes(photoId, buffer, writeLock, out string? error);

                // Hop back to the main server thread so SendPacket / SeenTouch run on the expected thread.
                if (sapi != null)
                {
                    sapi.Event.EnqueueMainThreadTask(() =>
                    {
                        if (ok)
                        {
                            _mod.PhotoSyncBridge.ServerTouchPhotoSeen(photoId);
                            SendServerTransferAck(fromPlayer, photoId, ok: true);
                        }
                        else
                        {
                            SendServerTransferAck(fromPlayer, photoId, ok: false, error ?? "Photo write failed");
                        }
                    }, "phototesting:UploadAck");
                }
            }, "phototesting:UploadWrite");
        }

        // Removes stale in-progress uploads so disconnects or failed transfers do not leak memory indefinitely.
        private void ServerMaybePruneIncoming(long nowMs)
        {
            int pruneIntervalMs = SyncCfg?.ServerPruneIntervalMs ?? 30_000;
            int uploadStaleMs = SyncCfg?.ServerUploadStaleMs ?? 120_000;

            lock (_serverIncomingLock)
            {
                if (_serverIncoming.Count == 0) return;
                if (_serverLastPruneMs != 0 && (nowMs - _serverLastPruneMs) < pruneIntervalMs) return;
                _serverLastPruneMs = nowMs;
            }

            PruneStaleIncomingAssemblies(_serverIncoming, _serverIncomingLock, nowMs, uploadStaleMs);
        }

        // Handles client download requests by reading the server-side png and streaming it back in chunks.
        // The disk read is dispatched to the thread pool so it does not block the packet handler thread;
        // chunk send and seen-touch run back on the main server thread.
        public void ServerHandleRequest(IServerPlayer fromPlayer, PhotoBlobRequestPacket packet)
        {
            if (_mod.Api == null || _mod.ServerChannel == null) return;
            if (fromPlayer == null || packet == null) return;

            // Per-player rate limit: protect disk + bandwidth from request floods.
            if (!ServerRequestThrottle.TryConsume(fromPlayer.PlayerUID, "req", Environment.TickCount64)) return;

            if (!TryNormalizePhotoId(packet.PhotoId, out string photoId)) return;

            // Path existence and size limit can both be checked off-thread inside TryLoadPhotoBytesForDownload.
            var sapi = _mod.Api as ICoreServerAPI;
            if (sapi == null) return;

            TyronThreadPool.QueueTask(() =>
            {
                bool ok = TryLoadPhotoBytesForDownload(photoId, out byte[]? bytes, out string? error);

                sapi.Event.EnqueueMainThreadTask(() =>
                {
                    if (!ok || bytes == null)
                    {
                        SendServerTransferAck(fromPlayer, photoId, ok: false, error);
                        return;
                    }

                    if (_mod.ServerChannel == null) return;
                    _mod.PhotoSyncBridge.ServerTouchPhotoSeen(photoId);
                    SendChunksConfigured(_mod.ServerChannel, fromPlayer, photoId, bytes, isUpload: false);
                }, "phototesting:DownloadDispatch");
            }, "phototesting:DownloadRead");
        }

        // Reassembles uploaded chunks from one client, validates the png, and persists it to the server photo store.
        public void ServerHandleChunk(IServerPlayer fromPlayer, PhotoBlobChunkPacket packet)
        {
            if (_mod.Api == null || _mod.ServerChannel == null) return;
            if (fromPlayer == null || packet == null) return;
            if (!packet.IsUpload) return; // ignore downloads on server

            long nowMs = Environment.TickCount64;
            ServerMaybePruneIncoming(nowMs);

            if (!TryNormalizeAndValidateChunkPacket(packet, out string photoId)) return;

            string playerUid = fromPlayer.PlayerUID;

            // Photo-id authorization: client must have legitimately captured this photo to upload bytes for it.
            if (!ExpectedUploads.IsExpected(playerUid, photoId, nowMs))
            {
                SendServerTransferAck(fromPlayer, photoId, ok: false, "Upload not authorized for this photo id");
                return;
            }

            // Isolate uploads per player so equal photo ids from different clients cannot collide in assembly state.
            string key = playerUid + "|" + photoId;

            // First-chunk gating: enforce per-player concurrent upload cap.
            bool isNewAssembly;
            lock (_serverIncomingLock)
            {
                isNewAssembly = !_serverIncoming.ContainsKey(key);
            }
            if (isNewAssembly)
            {
                int maxOpen = SyncCfg?.ServerMaxOpenUploadsPerPlayer ?? 2;
                if (!ExpectedUploads.TryBeginUpload(playerUid, maxOpen))
                {
                    SendServerTransferAck(fromPlayer, photoId, ok: false, "Too many concurrent uploads");
                    return;
                }
            }

            if (!TryProcessIncomingChunk(_serverIncoming, _serverIncomingLock, key, packet, nowMs, out IncomingAssembly? completed)
                || completed == null)
            {
                return;
            }

            // Completed: release accounting and consume the expected-upload entry (single-shot).
            // Note: if a player abandons mid-upload, their cap slot stays held until the assembly
            // ages out (~2 minutes). That self-locks only the offending player; not a server-wide concern.
            ExpectedUploads.EndUpload(playerUid);
            ExpectedUploads.Consume(playerUid, photoId);

            TryPersistUploadedPhoto(fromPlayer, photoId, completed);
        }

    }
}
