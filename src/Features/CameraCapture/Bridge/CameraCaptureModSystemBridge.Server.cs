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

            ConfigureServerPhotoSyncRuntime(api);
        }

        // Registers the remaining server packet handlers that route into sync, metadata, and config flows.
        private void ConfigureServerCameraCaptureSyncHandlers()
        {
            if (ServerChannel == null) return;

            ConfigureServerPhotoSyncTransferChannelHandlers();
            ConfigureServerPhotoMetadataChannelHandlers();
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
    // Shared server-side camera stack helpers for loaded-plate state persistence.
    // Centralizes attribute reads/writes so all camera packet handlers stay consistent.

        // Filters item stacks down to actual wetplate camera items before any camera-specific mutation runs.
        private static bool IsWetplateCameraStack(ItemStack? stack)
        {
            return stack?.Item is ItemWetplateCamera;
        }

        // Treats the string loaded-plate attribute as the authoritative quick check for whether a camera is loaded.
        internal static bool CameraHasLoadedPlate(ItemStack? cameraStack)
        {
            if (cameraStack == null) return false;
            string loaded = cameraStack.Attributes.GetString(ItemWetplateCamera.AttrLoadedPlate, string.Empty);
            return !string.IsNullOrWhiteSpace(loaded);
        }

        // Persists a single-item copy of the loaded plate onto the camera so later server actions can resolve it reliably.
        private static void SetLoadedPlateAttributes(ItemStack cameraStack, ItemStack loadedPlate)
        {
            ItemStack clone = loadedPlate.Clone();
            clone.StackSize = 1;

            cameraStack.Attributes.SetString(ItemWetplateCamera.AttrLoadedPlate, clone.Collectible?.Code?.ToString() ?? string.Empty);
            cameraStack.Attributes.SetItemstack(ItemWetplateCamera.AttrLoadedPlateStack, clone);
        }

        // Clears both legacy and current loaded-plate attributes when the camera becomes empty.
        private static void ClearLoadedPlateAttributes(ItemStack cameraStack)
        {
            cameraStack.Attributes.RemoveAttribute(ItemWetplateCamera.AttrLoadedPlate);
            cameraStack.Attributes.RemoveAttribute(ItemWetplateCamera.AttrLoadedPlateStack);
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
            if (WetPlateAttrs.IsDry(Api.World, loadedPlate)) return;

            float maxHoldStillSeconds = Config?.Viewfinder?.HoldStillDurationSeconds ?? 30f;
            if (maxHoldStillSeconds < 0f) maxHoldStillSeconds = 0f;
            float holdStillSeconds = ClampFiniteRange(packet.HoldStillSeconds, 0f, maxHoldStillSeconds);
            float holdStillMovement = ClampFiniteRange(packet.HoldStillMovement, 0f, 1000f);

            PlateStateService.SetStage(loadedPlate, PlateStage.Exposed);
            loadedPlate.Attributes.SetString(WetPlateAttrs.PhotoId, photoId);
            loadedPlate.Attributes.SetDouble(WetPlateAttrs.HoldStillSeconds, holdStillSeconds);
            loadedPlate.Attributes.SetDouble(WetPlateAttrs.HoldStillMovement, holdStillMovement);

            SetLoadedPlateAttributes(cameraStack, loadedPlate);
            SetCameraCode(cameraSlot, GetLoadedCameraCodeForPlate(loadedPlate));
            cameraSlot.MarkDirty();

            ServerTouchPhotoSeen(photoId);

            // Authorize the matching upload so the client's chunk packets are not rejected as unsolicited.
            _owner.PhotoSyncBridge.Runtime?.RegisterExpectedUpload(player.PlayerUID, photoId);
        }

        // Clamps packet-provided floats to a safe range, treating NaN/Infinity as the lower bound.
        private static float ClampFiniteRange(float value, float min, float max)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return min;
            return Math.Clamp(value, min, max);
        }
    // Authoritative server load/unload operations for camera and offhand plate transfer.
    // Applies eligibility checks and routes packet intent into stack mutations.

        // Moves one eligible offhand plate into the active camera and updates the camera item code to match the loaded plate state.
        private bool TryHandleCameraPlateLoad(IServerPlayer player)
        {
            if (Api?.World == null) return false;

            ItemSlot? cameraSlot = player.InventoryManager.ActiveHotbarSlot;
            ItemSlot? offhandSlot = player.InventoryManager.OffhandHotbarSlot;

            ItemStack? cameraStack = cameraSlot?.Itemstack;
            ItemStack? offhandStack = offhandSlot?.Itemstack;

            if (!IsWetplateCameraStack(cameraStack) || offhandStack == null) return false;
            if (cameraSlot == null || offhandSlot == null || cameraStack == null) return false;
            if (CameraHasLoadedPlate(cameraStack)) return false;
            if (!CameraPlateEligibility.CanLoadIntoCamera(offhandStack)) return false;

            ItemStack loadedPlate = offhandStack.Clone();
            loadedPlate.StackSize = 1;

            SetLoadedPlateAttributes(cameraStack, loadedPlate);
            SetCameraCode(cameraSlot, GetLoadedCameraCodeForPlate(loadedPlate));

            offhandSlot.TakeOut(1);
            offhandSlot.MarkDirty();
            cameraSlot.MarkDirty();

            AudioUtils.FireAndForgetEntitySound(Api?.World, _cameraPlateLoadSound, player.Entity, AudioUtils.NextRandomPitch(Api?.World));
            return true;
        }

        // Pulls the loaded plate back out of the active camera into an empty offhand slot and restores the base camera variant.
        private bool TryHandleCameraPlateUnload(IServerPlayer player)
        {
            if (Api?.World == null) return false;

            ItemSlot? cameraSlot = player.InventoryManager.ActiveHotbarSlot;
            ItemSlot? offhandSlot = player.InventoryManager.OffhandHotbarSlot;

            ItemStack? cameraStack = cameraSlot?.Itemstack;
            if (!IsWetplateCameraStack(cameraStack)) return false;
            if (offhandSlot == null || !offhandSlot.Empty) return false;
            if (cameraSlot == null || cameraStack == null) return false;
            if (!CameraHasLoadedPlate(cameraStack)) return false;

            if (!CameraItemHelper.TryGetLoadedPlateStack(cameraStack, Api.World, out ItemStack? loadedPlate) || loadedPlate == null)
            {
                ClearLoadedPlateAttributes(cameraStack);
                SetCameraCode(cameraSlot, _wetplateCameraBaseCode);
                cameraSlot.MarkDirty();
                return true;
            }

            loadedPlate.StackSize = 1;
            ClearLoadedPlateAttributes(cameraStack);
            SetCameraCode(cameraSlot, _wetplateCameraBaseCode);
            cameraSlot.MarkDirty();

            offhandSlot.Itemstack = loadedPlate;
            offhandSlot.MarkDirty();

            AudioUtils.FireAndForgetEntitySound(Api?.World, _cameraPlateUnloadSound, player.Entity, AudioUtils.NextRandomPitch(Api?.World));
            return true;
        }

        // Server packet entry point for explicit camera load and unload requests from client input code.
        private void OnCameraLoadPlateReceived(IServerPlayer player, CameraLoadPlatePacket packet)
        {
            if (Api?.Side != EnumAppSide.Server || player == null || packet == null) return;

            if (packet.Load)
            {
                TryHandleCameraPlateLoad(player);
                return;
            }

            TryHandleCameraPlateUnload(player);
        }
    // Camera visual/state presentation helpers and non-blocking camera sound playback.
    // Keeps cosmetic updates isolated from gameplay mutation rules.

        private static readonly AssetLocation _sensitizedPlateItemCode = new AssetLocation("phototesting", "sensitizedplate");
        private static readonly AssetLocation _wetplateCameraBaseCode = new AssetLocation("phototesting", "wetplatecamera");
        // Asset path remains "loaded-silvered" for backward compatibility; gameplay semantics are sensitized.
        private static readonly AssetLocation _wetplateCameraLoadedSensitizedCode = new AssetLocation("phototesting", "wetplatecamera-loaded-silvered");
        private static readonly AssetLocation _wetplateCameraLoadedExposedCode = new AssetLocation("phototesting", "wetplatecamera-loaded-exposed");
        private static readonly AssetLocation _cameraPlateLoadSound = new AssetLocation("phototesting", "sounds/glass-slide1");
        private static readonly AssetLocation _cameraPlateUnloadSound = new AssetLocation("phototesting", "sounds/glass-slide2");

        // Chooses the camera item variant that matches the currently loaded plate's visible state.
        private static AssetLocation GetLoadedCameraCodeForPlate(ItemStack? loadedPlate)
        {
            if (CameraPlateEligibility.IsPlateExposedForCameraVisual(loadedPlate)) return _wetplateCameraLoadedExposedCode;
            return _wetplateCameraLoadedSensitizedCode;
        }

        // Swaps the active camera item code without losing loaded-plate attributes or stack count.
        private void SetCameraCode(ItemSlot cameraSlot, AssetLocation code)
        {
            if (Api == null || cameraSlot.Itemstack == null) return;
            if (cameraSlot.Itemstack.Collectible?.Code == code) return;

            Item? item = Api.World.GetItem(code);
            if (item == null) return;

            ItemStack replacement = new ItemStack(item, cameraSlot.Itemstack.StackSize);
            replacement.Attributes.MergeTree(cameraSlot.Itemstack.Attributes.Clone());
            cameraSlot.Itemstack = replacement;
            cameraSlot.MarkDirty();
        }

    }
}
