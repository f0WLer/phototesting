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

        // Resolves the correct base code for a camera stack, falling back to the standard wetplate code.
        private static AssetLocation GetBaseCode(ItemStack? cameraStack)
            => cameraStack?.Item is ItemWetplateCamera cam ? cam.CameraBaseCode : _wetplateCameraBaseCode;

        // Chooses the camera item variant that matches the currently loaded plate's visible state.
        private static AssetLocation GetLoadedCameraCodeForPlate(ItemStack? cameraStack, ItemStack? loadedPlate)
        {
            bool exposed = PlateStateService.IsPlateExposed(loadedPlate);
            return cameraStack?.Item is ItemWetplateCamera cam
                ? (exposed ? cam.CameraLoadedExposedCode : cam.CameraLoadedSensitizedCode)
                : (exposed ? _wetplateCameraLoadedExposedCode : _wetplateCameraLoadedSensitizedCode);
        }

        private ItemStack? ReplaceCameraCode(ItemStack? cameraStack, AssetLocation code)
        {
            if (Api == null || cameraStack == null) return cameraStack;
            if (cameraStack.Collectible?.Code == code) return cameraStack;

            Item? item = Api.World.GetItem(code);
            if (item == null) return cameraStack;

            ItemStack replacement = new ItemStack(item, cameraStack.StackSize);
            replacement.Attributes.MergeTree(cameraStack.Attributes.Clone());
            return replacement;
        }

        // Swaps the active camera item code without losing loaded-plate attributes or stack count.
        private void SetCameraCode(ItemSlot cameraSlot, AssetLocation code)
        {
            ItemStack? replacement = ReplaceCameraCode(cameraSlot.Itemstack, code);
            if (replacement == null) return;
            cameraSlot.Itemstack = replacement;
            cameraSlot.MarkDirty();
        }
    }
}