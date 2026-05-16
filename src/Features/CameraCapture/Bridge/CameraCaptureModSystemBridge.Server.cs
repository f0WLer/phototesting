using Phototesting.AdminTooling;
using Phototesting.CameraCapture.Contracts;
using Phototesting.CameraCapture.Integration;
using Phototesting.PhotoSync.Storage;
using Phototesting.PlateLifecycle;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Phototesting.CameraCapture
{
    // Server-authoritative bridge: startup wiring, capture config, exposure, plate I/O, visuals, and shared state.
    internal sealed partial class CameraCaptureModSystemBridge
    {
        // Composes full server-side camera-capture startup so ModSystem root stays declarative.
        internal void ConfigureServerCameraCaptureStartup(ICoreServerAPI api)
        {
            ConfigureServerCameraCaptureCore(api);
            ConfigureServerCameraCaptureSyncHandlers();
            BroadcastServerCameraCaptureConfig(api);
        }

        // Wires server-side camera authority handlers and delegates sync runtime composition.
        private void ConfigureServerCameraCaptureCore(ICoreServerAPI api)
        {
            ServerChannel = api.Network.GetChannel("phototesting");
            CameraCaptureChannelRegistration.ConfigureServerCoreHandlers(ServerChannel, OnPhotoTakenReceived, OnCameraLoadPlateReceived);

            _owner.PhotoSyncBridge.ConfigureServerPhotoSyncRuntime(api);
        }

        // Registers the remaining server packet handlers that route into sync, metadata, and config flows.
        private void ConfigureServerCameraCaptureSyncHandlers()
        {
            if (ServerChannel == null) return;

            _owner.PhotoSyncBridge.ConfigureServerPhotoSyncTransferChannelHandlers();
            _owner.PhotoSyncBridge.ConfigureServerPhotoSeenChannelHandler();
            CameraCaptureChannelRegistration.ConfigureServerSyncHandlers(
                ServerChannel,
                (player, p) => OnPhotoCaptureConfigRequested(player));
        }
    // Server-authoritative camera capture config broadcast and request handling.

        // Broadcasts the current server-authoritative capture config to connected players during startup/hot-reload.
        private void BroadcastServerCameraCaptureConfig(ICoreServerAPI api)
        {
            // Send once on startup for currently connected players (mainly relevant on hot-reload).
            foreach (IServerPlayer player in api.World.AllOnlinePlayers)
            {
                OnPhotoCaptureConfigRequested(player);
            }
        }

        // Responds to client config sync requests with the server-authoritative capture maximum dimension.
        private void OnPhotoCaptureConfigRequested(IServerPlayer? player)
        {
            if (player == null || ServerChannel == null) return;

            int maxDimension = GetServerPhotoCaptureMaxDimension();
            ServerChannel.SendPacket(new PhotoCaptureConfigPacket
            {
                MaxDimension = maxDimension
            }, player);
        }

        // Resolves and clamps the server-side capture resolution limit before broadcasting it.
        private int GetServerPhotoCaptureMaxDimension()
        {
            Config = OperatorToolingConfigLifecycle.EnsureNormalized(Config);
            Config.Viewfinder.ClampInPlace();
            return Config.Viewfinder.PhotoCaptureMaxDimension;
        }
    // Authoritative server capture-finalization path for loaded camera plates.
    // Converts sensitized plates to exposed state and records normalized capture metadata.

        // Authoritatively converts the camera's loaded sensitized plate into an exposed plate after capture completes.
        private void OnPhotoTakenReceived(IServerPlayer player, PhotoTakenPacket packet)
        {
            if (Api?.Side != EnumAppSide.Server || Api.World == null) return;
            if (player == null || packet == null) return;

            string photoId = PhotoAssetStoragePaths.NormalizePhotoId(packet.PhotoId);
            if (string.IsNullOrWhiteSpace(photoId)) return;

            ItemSlot? cameraSlot = player.InventoryManager.ActiveHotbarSlot;
            ItemStack? cameraStack = cameraSlot?.Itemstack;
            if (!IsWetplateCameraStack(cameraStack) || !CameraHasLoadedPlate(cameraStack)) return;
            if (cameraSlot == null || cameraStack == null) return;

            if (!CameraItemHelper.TryGetLoadedPlateStack(cameraStack, Api.World, out ItemStack? loadedPlate) || loadedPlate == null) return;
            if (!CameraPlateEligibility.IsPlateSensitizedForExposure(loadedPlate)) return;
            if (PlateDryingTransition.IsDry(Api.World, loadedPlate)) return;

            float maxHoldStillSeconds = Config?.Viewfinder?.HoldStillDurationSeconds ?? 30f;
            if (maxHoldStillSeconds < 0f) maxHoldStillSeconds = 0f;
            float holdStillSeconds = ClampFiniteRange(packet.HoldStillSeconds, 0f, maxHoldStillSeconds);
            float holdStillMovement = ClampFiniteRange(packet.HoldStillMovement, 0f, 1000f);

            PlateStateService.SetStage(loadedPlate, PlateStage.Exposed);
            loadedPlate.Attributes.SetString(PlateAttrs.PhotoId, photoId);
            loadedPlate.Attributes.SetDouble(PlateAttrs.HoldStillSeconds, holdStillSeconds);
            loadedPlate.Attributes.SetDouble(PlateAttrs.HoldStillMovement, holdStillMovement);

            SetLoadedPlateAttributes(cameraStack, loadedPlate);
            SetCameraCode(cameraSlot, GetLoadedCameraCodeForPlate(loadedPlate));
            cameraSlot.MarkDirty();

            _owner.PhotoSyncBridge.ServerTouchPhotoSeen(photoId);

            // Authorize the matching upload so the client's chunk packets are not rejected as unsolicited.
            _owner.PhotoSyncBridge.Runtime?.RegisterExpectedUpload(player.PlayerUID, photoId);
        }

        // Clamps packet-provided floats to a safe range, treating NaN/Infinity as the lower bound.
        private static float ClampFiniteRange(float value, float min, float max)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return min;
            return Math.Clamp(value, min, max);
        }
    }
}
