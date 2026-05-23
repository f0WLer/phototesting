using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Phototesting.CameraCapture;

namespace Phototesting
{
    /// <summary>
    /// Shared camera-item access helpers used by client and server camera flows.
    /// </summary>
    public static class CameraItemHelper
    {
        public const string MountedTripodCodeAttrKey = "phototestingMountedTripodCode";

        // Gets the currently active hotbar slot only when it holds the wetplate camera.
        public static ItemSlot? GetActiveCameraSlot(ICoreClientAPI? capi)
        {
            ItemSlot? activeSlot = capi?.World?.Player?.InventoryManager?.ActiveHotbarSlot;
            return activeSlot?.Itemstack?.Item is ItemWetplateCamera ? activeSlot : null;
        }

        // Gets the currently active camera stack, or null when the active slot is not the camera.
        public static ItemStack? GetActiveCameraStack(ICoreClientAPI? capi)
        {
            return GetActiveCameraSlot(capi)?.Itemstack;
        }

        // Returns true when the camera stack currently carries an attached tripod code.
        public static bool HasMountedTripod(ItemStack? cameraStack)
        {
            if (cameraStack?.Item is not ItemWetplateCamera) return false;
            return !string.IsNullOrEmpty(cameraStack.Attributes.GetString(MountedTripodCodeAttrKey, string.Empty));
        }

        // Rehydrates the loaded plate from full stored stack first, then legacy code-only attribute.
        public static bool TryGetLoadedPlateStack(ItemStack? cameraStack, IWorldAccessor? world, out ItemStack? loadedPlate, Action<Exception>? onStoredStackReadFailure = null)
        {
            loadedPlate = null;
            if (cameraStack?.Item is not ItemWetplateCamera || world == null) return false;

            try
            {
                loadedPlate = cameraStack.Attributes.GetItemstack(ItemWetplateCamera.AttrLoadedPlateStack, null);
                loadedPlate?.ResolveBlockOrItem(world);
                if (loadedPlate != null) return true;
            }
            catch (Exception ex)
            {
                loadedPlate = null;
                onStoredStackReadFailure?.Invoke(ex);
            }

            string loadedCode = cameraStack.Attributes.GetString(ItemWetplateCamera.AttrLoadedPlate, string.Empty) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(loadedCode)) return false;

            Item? loadedItem = world.GetItem(new AssetLocation(loadedCode));
            if (loadedItem == null) return false;

            loadedPlate = new ItemStack(loadedItem, 1);
            return true;
        }
    }
}
