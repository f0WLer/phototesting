using Phototesting.AdminTooling;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Phototesting.CameraCapture
{
    public sealed class BlockMountedCamera : Block
    {
        public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f)
        {
            return Array.Empty<ItemStack>();
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (byPlayer == null || blockSel == null) return false;
            if (world.Side == EnumAppSide.Client) return true;

            PhotoTestingModSystem? modSys = PhotoTestingConfigAccess.ResolveModSystem(world.Api);
            if (modSys == null) return false;

            bool shiftDown = byPlayer?.Entity?.Controls?.ShiftKey == true || byPlayer?.Entity?.Controls?.Sneak == true;
            return modSys.CameraCaptureBridge.TryHandleMountedCameraBlockInteract(world, blockSel.Position, byPlayer, shiftDown);
        }

        public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f)
        {
            if (world.Side == EnumAppSide.Server)
            {
                PhotoTestingModSystem? modSys = PhotoTestingConfigAccess.ResolveModSystem(world.Api);
                modSys?.CameraCaptureBridge.HandleMountedCameraBlockBroken(world, pos, byPlayer);
            }

            base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
        }
    }
}