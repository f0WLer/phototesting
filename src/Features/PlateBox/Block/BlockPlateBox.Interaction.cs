using Vintagestory.API.Common;

namespace Phototesting.PlateBox
{
    public sealed partial class BlockPlateBox
    {
        // Keeps engine callback ownership local and delegates heavy interaction branching.
        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (world == null || byPlayer == null || blockSel?.Position == null) return false;
            if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BlockEntityPlateBox be) return false;
            return HandlePlateBoxInteractionStart(world, byPlayer, blockSel, be);
        }
    }
}