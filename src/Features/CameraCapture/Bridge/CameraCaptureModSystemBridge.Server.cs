using Phototesting.AdminTooling;
using Phototesting.CameraCapture.Contracts;
using Phototesting.CameraCapture.Integration;
using Phototesting.PhotoSync.Storage;
using Phototesting.PlateLifecycle;
using Phototesting.PlateLifecycle.Tray;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
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
            CameraCaptureChannelRegistration.ConfigureServerCoreHandlers(ServerChannel, OnPhotoTakenReceived, OnCameraLoadPlateReceived, OnCameraTripodReceived, OnExposureStateReceived, OnCameraMountRequestReceived);

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

            ServerChannel.SetMessageHandler<SealAndInsertIntoTrayPacket>(OnSealAndInsertTrayReceived);
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

            if (!TryResolveCameraStorage(player, out ItemSlot? cameraSlot, out ItemStack? cameraStack, out BlockEntityMountedCamera? mountedBe)) return;
            if (cameraStack == null || !CameraHasLoadedPlate(cameraStack)) return;

            if (!CameraItemHelper.TryGetLoadedPlateStack(cameraStack, Api.World, out ItemStack? loadedPlate) || loadedPlate == null) return;

            // Accept Sensitized, Exposing, or ExposurePaused plates for final sealing.
            PlateStage stage = PlateStateService.GetStage(loadedPlate);
            if (stage != PlateStage.Sensitized && stage != PlateStage.Exposing && stage != PlateStage.ExposurePaused) return;

            if (PlateDryingTransition.IsDry(Api.World, loadedPlate)) return;

            PlateStateService.SetStage(loadedPlate, PlateStage.Exposed);
            loadedPlate.Attributes.SetString("photoId", photoId);
            loadedPlate.Attributes.RemoveAttribute(PlateStateAttributes.ExposureId);
            loadedPlate.Attributes.RemoveAttribute(PlateStateAttributes.ExposedFrames);
            loadedPlate.Attributes.RemoveAttribute(PlateStateAttributes.ExposureTargetFrames);
            CameraItemHelper.ClearMountedCaptureState(cameraStack);

            SetLoadedPlateAttributes(cameraStack, loadedPlate);
            if (mountedBe != null)
            {
                ItemStack? updatedCamera = ReplaceCameraCode(cameraStack, GetLoadedCameraCodeForPlate(loadedPlate));
                if (updatedCamera == null) return;
                mountedBe.SetStoredCameraStack(updatedCamera, mountedBe.OwnerPlayerUid, Api.World);
            }
            else if (cameraSlot != null)
            {
                SetCameraCode(cameraSlot, GetLoadedCameraCodeForPlate(loadedPlate));
                cameraSlot.MarkDirty();
            }

            _owner.PhotoSyncBridge.ServerTouchPhotoSeen(photoId);

            // Authorize the matching upload so the client's chunk packets are not rejected as unsolicited.
            _owner.PhotoSyncBridge.Runtime?.RegisterExpectedUpload(player.PlayerUID, photoId);
        }

        // Authoritatively updates plate stage when accumulation starts, pauses, or resumes.
        private void OnExposureStateReceived(IServerPlayer player, ExposureStatePacket packet)
        {
            if (Api?.Side != EnumAppSide.Server || Api.World == null) return;
            if (player == null || packet == null) return;

            if (!TryResolveCameraStorage(player, out ItemSlot? cameraSlot, out ItemStack? cameraStack, out BlockEntityMountedCamera? mountedBe)) return;
            if (cameraStack == null || !CameraHasLoadedPlate(cameraStack)) return;

            if (!CameraItemHelper.TryGetLoadedPlateStack(cameraStack, Api.World, out ItemStack? loadedPlate) || loadedPlate == null) return;

            PlateStage stage = PlateStateService.GetStage(loadedPlate);

            if (packet.IsExposing)
            {
                // Plate must be exposable to transition to Exposing.
                if (stage != PlateStage.Sensitized && stage != PlateStage.ExposurePaused && stage != PlateStage.Exposing) return;

                PlateStateService.SetStage(loadedPlate, PlateStage.Exposing);
                if (!string.IsNullOrEmpty(packet.ExposureId))
                    loadedPlate.Attributes.SetString(PlateStateAttributes.ExposureId, packet.ExposureId);
                if (packet.TargetFrames > 0)
                    loadedPlate.Attributes.SetInt(PlateStateAttributes.ExposureTargetFrames, packet.TargetFrames);
            }
            else
            {
                // Pausing: accept only from Exposing.
                if (stage != PlateStage.Exposing && stage != PlateStage.ExposurePaused) return;

                PlateStateService.SetStage(loadedPlate, PlateStage.ExposurePaused);
                loadedPlate.Attributes.SetInt(PlateStateAttributes.ExposedFrames, packet.ExposedFrames);
            }

            SetLoadedPlateAttributes(cameraStack, loadedPlate);
            if (mountedBe != null)
                mountedBe.MarkCameraDirty();
            else
                cameraSlot?.MarkDirty();
        }

        // Stamps the in-tray paused/developing/finished plate with the sealed photo id.
        // When the developer pour has not advanced yet, transition ExposurePaused to Exposed so the
        // pending developer pour sees the correct stage. Later arrivals keep the current stage intact.
        private void OnSealAndInsertTrayReceived(IServerPlayer player, SealAndInsertIntoTrayPacket packet)
        {
            if (Api?.Side != EnumAppSide.Server || Api.World == null) return;
            if (player == null || packet == null) return;

            string photoId = PhotoAssetStoragePaths.NormalizePhotoId(packet.PhotoId);
            if (string.IsNullOrWhiteSpace(photoId)) return;

            BlockPos trayPos = new BlockPos(packet.TrayPosX, packet.TrayPosY, packet.TrayPosZ, packet.TrayPosDim);
            if (Api.World.BlockAccessor.GetBlockEntity(trayPos) is not BlockEntityDevelopmentTray be) return;

            ItemStack? trayPlate = be.PlateStack;
            if (trayPlate == null) return;

            PlateStage trayStage = PlateStateService.GetStage(trayPlate);
            if (trayStage != PlateStage.ExposurePaused
                && trayStage != PlateStage.Developing
                && trayStage != PlateStage.Developed
                && trayStage != PlateStage.Finished) return;

            string exposureId = trayPlate.Attributes.GetString(PlateStateAttributes.ExposureId) ?? string.Empty;
            if (!string.Equals(exposureId, packet.ExposureId, StringComparison.OrdinalIgnoreCase)) return;

            trayPlate.Attributes.SetString("photoId", photoId);
            trayPlate.Attributes.RemoveAttribute(PlateStateAttributes.ExposureId);
            trayPlate.Attributes.RemoveAttribute(PlateStateAttributes.ExposedFrames);
            trayPlate.Attributes.RemoveAttribute(PlateStateAttributes.ExposureTargetFrames);
            // Only change stage for ExposurePaused; if the tray already advanced to Developing/Developed/Finished,
            // just set the photoId and clean up exposure attrs without rewinding the stage.
            if (trayStage == PlateStage.ExposurePaused)
                PlateStateService.SetStage(trayPlate, PlateStage.Exposed);
            be.TrySetPlate(trayPlate);

            _owner.PhotoSyncBridge.ServerTouchPhotoSeen(photoId);
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
