using Phototesting.PlateLifecycle;
using Vintagestory.API.Common;

namespace Phototesting.CameraCapture
{
    internal sealed partial class CameraCaptureModSystemBridge
    {
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
            if (PlateStateService.IsPlateExposed(loadedPlate)) return _wetplateCameraLoadedExposedCode;
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