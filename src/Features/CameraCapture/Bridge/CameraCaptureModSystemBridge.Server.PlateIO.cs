using Phototesting.CameraCapture.Contracts;
using Phototesting.PlateLifecycle;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Phototesting.CameraCapture
{
    internal sealed partial class CameraCaptureModSystemBridge
    {
    // Shared server-side camera stack helpers for loaded-plate and tripod state persistence.
    // Centralizes attribute reads/writes and world mutations so all camera packet handlers stay consistent.

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

        // Parses the stored mounted-camera position attribute back into a block position.
        private static bool TryReadMountedCameraPos(ItemStack cameraStack, out BlockPos pos)
        {
            pos = new BlockPos(0, 0, 0);
            string[] parts = cameraStack.Attributes.GetString(CameraItemHelper.MountedPosAttrKey, string.Empty).Split(',');
            if (parts.Length != 3 || !int.TryParse(parts[0], out int x) || !int.TryParse(parts[1], out int y) || !int.TryParse(parts[2], out int z)) return false;
            pos = new BlockPos(x, y, z);
            return true;
        }

        // Stores the mounted-camera block position on the camera stack for later cleanup.
        private static void SetMountedCameraPos(ItemStack cameraStack, BlockPos pos)
        {
            cameraStack.Attributes.SetString(CameraItemHelper.MountedPosAttrKey, $"{pos.X},{pos.Y},{pos.Z}");
        }

        // Clears the mounted-camera block position attribute from the camera stack.
        private static void ClearMountedCameraPos(ItemStack cameraStack)
        {
            cameraStack.Attributes.RemoveAttribute(CameraItemHelper.MountedPosAttrKey);
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

        // Moves the tripod item from the offhand slot onto the active camera and records its mounted state.
        private bool TryHandleCameraTripodMount(IServerPlayer player)
        {
            if (Api?.World == null) return false;

            ItemSlot? cameraSlot = player.InventoryManager.ActiveHotbarSlot;
            ItemSlot? offhandSlot = player.InventoryManager.OffhandHotbarSlot;

            ItemStack? cameraStack = cameraSlot?.Itemstack;
            ItemStack? offhandStack = offhandSlot?.Itemstack;

            if (!IsWetplateCameraStack(cameraStack) || cameraSlot == null || offhandSlot == null || offhandStack == null) return false;
            if (CameraHasLoadedPlate(cameraStack)) return false;
            if (CameraItemHelper.HasMountedTripod(cameraStack)) return false;
            if (!CameraItemHelper.IsTripodItemStack(offhandStack)) return false;

            cameraStack!.Attributes.SetString(CameraItemHelper.MountedAttrKey, offhandStack.Collectible?.Code?.ToString() ?? CameraItemHelper.TripodItemCode.ToString());
            offhandSlot.TakeOut(1);
            offhandSlot.MarkDirty();
            cameraSlot.MarkDirty();
            return true;
        }

        // Returns the mounted tripod item to an empty offhand slot and clears the camera's mounted marker.
        private bool TryHandleCameraTripodUnmount(IServerPlayer player)
        {
            if (Api?.World == null) return false;

            ItemSlot? cameraSlot = player.InventoryManager.ActiveHotbarSlot;
            ItemSlot? offhandSlot = player.InventoryManager.OffhandHotbarSlot;

            ItemStack? cameraStack = cameraSlot?.Itemstack;
            if (!IsWetplateCameraStack(cameraStack) || cameraSlot == null || offhandSlot == null) return false;
            if (!offhandSlot.Empty) return false;
            if (CameraHasLoadedPlate(cameraStack)) return false;
            if (!CameraItemHelper.HasMountedTripod(cameraStack)) return false;

            string tripodCode = cameraStack!.Attributes.GetString(CameraItemHelper.MountedAttrKey, string.Empty);
            if (string.IsNullOrWhiteSpace(tripodCode)) tripodCode = CameraItemHelper.TripodItemCode.ToString();

            Item? tripodItem = Api.World.GetItem(new AssetLocation(tripodCode)) ?? Api.World.GetItem(CameraItemHelper.TripodItemCode);
            if (tripodItem == null) return false;

            offhandSlot.Itemstack = new ItemStack(tripodItem, 1);
            offhandSlot.MarkDirty();

            cameraStack.Attributes.RemoveAttribute(CameraItemHelper.MountedAttrKey);
            cameraSlot.MarkDirty();
            return true;
        }

        // Server packet entry point for explicit tripod attach and detach requests from the client input code.
        private void OnCameraTripodReceived(IServerPlayer player, CameraTripodPacket packet)
        {
            if (Api?.Side != EnumAppSide.Server || player == null || packet == null) return;

            if (packet.Mount)
            {
                TryHandleCameraTripodMount(player);
                return;
            }

            TryHandleCameraTripodUnmount(player);
        }

        // Places the mounted camera block at the player's standing position and remembers the location on the camera stack.
        private void EnsureMountedCameraBlock(ItemStack cameraStack, IServerPlayer player)
        {
            if (Api?.World == null || player?.Entity == null) return;

            if (TryReadMountedCameraPos(cameraStack, out BlockPos existingPos))
            {
                Block existing = Api.World.BlockAccessor.GetBlock(existingPos);
                if (existing?.Code == CameraItemHelper.MountedCameraBlockCode) return;
            }

            BlockPos pos = player.Entity.SidedPos.AsBlockPos;
            Block current = Api.World.BlockAccessor.GetBlock(pos);
            if (current.Replaceable < 6000)
            {
                pos = pos.UpCopy();
                current = Api.World.BlockAccessor.GetBlock(pos);
                if (current.Replaceable < 6000) return;
            }

            Block mountedBlock = Api.World.GetBlock(CameraItemHelper.MountedCameraBlockCode);
            if (mountedBlock == null) return;

            Api.World.BlockAccessor.SetBlock(mountedBlock.BlockId, pos);
            SetMountedCameraPos(cameraStack, pos);
        }

        // Removes the mounted camera block if the camera currently remembers one.
        private void ClearMountedCameraBlock(ItemStack cameraStack)
        {
            if (Api?.World == null) return;
            if (!TryReadMountedCameraPos(cameraStack, out BlockPos pos)) return;

            Block block = Api.World.BlockAccessor.GetBlock(pos);
            if (block?.Code == CameraItemHelper.MountedCameraBlockCode)
                Api.World.BlockAccessor.SetBlock(0, pos);

            ClearMountedCameraPos(cameraStack);
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