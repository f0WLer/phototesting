using Vintagestory.API.Client;
using Vintagestory.API.Server;
using Phototesting.PhotoSync.Contracts;
using Phototesting.PhotoSync.Runtime;

namespace Phototesting.PhotoSync.Integration
{
    // PhotoSync startup composition and packet handler wiring.
    // Keeps sync runtime and sync handler registration out of camera bootstrap methods.
    internal sealed partial class PhotoSyncModSystemBridge
    {
        private readonly PhotoTestingModSystem _owner;
        internal PhotoAssetSync? Runtime;

        internal PhotoSyncModSystemBridge(PhotoTestingModSystem owner)
        {
            _owner = owner;
        }

        // Ensures feature runtime exists before handler registration or side-specific startup uses it.
        private PhotoAssetSync GetOrCreatePhotoSyncRuntime()
        {
            return Runtime ??= new PhotoAssetSync(_owner);
        }

        // Registers PhotoSync packet DTOs on the shared channel, preserving wire-order invariants.
        internal static INetworkChannel RegisterPhotoSyncMessageTypes(INetworkChannel channel)
        {
            return channel
                .RegisterMessageType(typeof(PhotoBlobRequestPacket))
                .RegisterMessageType(typeof(PhotoBlobChunkPacket))
                .RegisterMessageType(typeof(PhotoBlobAckPacket))
                .RegisterMessageType(typeof(PhotoCaptionSetPacket))
                .RegisterMessageType(typeof(PhotoSeenPacket));
        }

        // Composes client-side PhotoSync startup for runtime state needed before handler wiring.
        internal void ConfigureClientPhotoSyncStartup()
        {
            GetOrCreatePhotoSyncRuntime();
        }

        // Registers client handlers for sync transfer packets.
        internal void ConfigureClientPhotoSyncTransferChannelHandlers()
        {
            if (_owner.ClientChannel == null) return;

            _owner.ClientChannel
                .SetMessageHandler<PhotoBlobChunkPacket>(HandleClientPhotoSyncChunkPacket)
                .SetMessageHandler<PhotoBlobAckPacket>(HandleClientPhotoSyncAckPacket);
        }

        // Integration-owned wrapper keeps channel wiring decoupled from direct runtime method references.
        private void HandleClientPhotoSyncChunkPacket(PhotoBlobChunkPacket packet)
        {
            GetOrCreatePhotoSyncRuntime().ClientHandleChunk(packet);
        }

        // Integration-owned wrapper keeps channel wiring decoupled from direct runtime method references.
        private void HandleClientPhotoSyncAckPacket(PhotoBlobAckPacket packet)
        {
            GetOrCreatePhotoSyncRuntime().ClientHandleAck(packet);
        }

        // Initializes server-side photo-sync runtime service and maintenance listeners.
        internal void ConfigureServerPhotoSyncRuntime(ICoreServerAPI api)
        {
            PhotoAssetSync runtime = GetOrCreatePhotoSyncRuntime();
            _serverPhotoSyncPruneListenerId = api.Event.RegisterGameTickListener(_ => runtime.ServerPruneTick(Environment.TickCount64), 10_000);

            _serverPhotoSeenService = ServerPhotoSeenService.LoadOrCreate(api, PhotoTestingModSystem.ServerPhotoIndexFileName);
            _serverPhotoLastSeenFlushListenerId = api.Event.RegisterGameTickListener(_ => _serverPhotoSeenService?.TryFlush(api), 10_000);
        }

        // Registers server handlers for sync transfer packets.
        internal void ConfigureServerPhotoSyncTransferChannelHandlers()
        {
            if (_owner.ServerChannel == null) return;

            _owner.ServerChannel
                .SetMessageHandler<PhotoBlobRequestPacket>(HandleServerPhotoSyncRequestPacket)
                .SetMessageHandler<PhotoBlobChunkPacket>(HandleServerPhotoSyncChunkPacket);
        }

        // Integration-owned wrapper keeps channel wiring decoupled from direct runtime method references.
        private void HandleServerPhotoSyncRequestPacket(IServerPlayer player, PhotoBlobRequestPacket packet)
        {
            GetOrCreatePhotoSyncRuntime().ServerHandleRequest(player, packet);
        }

        // Integration-owned wrapper keeps channel wiring decoupled from direct runtime method references.
        private void HandleServerPhotoSyncChunkPacket(IServerPlayer player, PhotoBlobChunkPacket packet)
        {
            GetOrCreatePhotoSyncRuntime().ServerHandleChunk(player, packet);
        }

        // Owns server-side sync/metadata maintenance teardown that was registered during feature startup.
        internal void DisposeServerPhotoSyncAndMetadataRuntime(ICoreServerAPI sapi)
        {
            if (_serverPhotoLastSeenFlushListenerId.HasValue && _serverPhotoLastSeenFlushListenerId.Value > 0)
            {
                long id = _serverPhotoLastSeenFlushListenerId.Value;
                BestEffort.Try(_owner.BestEffortLogger, "unregister server photo last-seen flush listener", () => sapi.Event.UnregisterGameTickListener(id));
                _serverPhotoLastSeenFlushListenerId = null;
            }

            if (_serverPhotoSyncPruneListenerId.HasValue && _serverPhotoSyncPruneListenerId.Value > 0)
            {
                long id = _serverPhotoSyncPruneListenerId.Value;
                BestEffort.Try(_owner.BestEffortLogger, "unregister server photo sync prune listener", () => sapi.Event.UnregisterGameTickListener(id));
                _serverPhotoSyncPruneListenerId = null;
            }

            BestEffort.Try(_owner.BestEffortLogger, "flush server photo last-seen index on dispose", () => _serverPhotoSeenService?.TryFlush(sapi));
        }

        // Clears feature-owned sync/metadata runtime references during mod shutdown.
        internal void ClearPhotoSyncAndMetadataRuntimeReferences()
        {
            Runtime = null;
            _serverPhotoSeenService = null;
        }
    }
}
