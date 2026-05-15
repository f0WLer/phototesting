using Phototesting.CameraCapture.Contracts;
using Phototesting.PlateLifecycle;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Phototesting.CameraCapture
{
    internal sealed partial class CameraCaptureModSystemBridge
    {
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
    }
}