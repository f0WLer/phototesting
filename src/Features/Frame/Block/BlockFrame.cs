using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Phototesting.PhotoMetadata.Model;

namespace Phototesting.Frame
{
    // Photo-frame placeable block. Holds a single photo-bearing item
    // (any stack carrying a non-empty PhotographAttrs.PhotoId attribute, e.g. a finished photo plate).
    // Right-click with such an item to insert; right-click while populated retrieves it.
    // Display rendering is delegated to BlockEntityFrame which uses PhotoPlateRenderUtil to resolve the
    // photo atlas position via the shared PhotoSync storage layer.
    public class BlockFrame : Block
    {
        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            BlockEntityFrame? be = world.BlockAccessor.GetBlockEntity(blockSel.Position) as BlockEntityFrame;
            if (be != null && be.OnInteract(world, byPlayer))
            {
                return true;
            }
            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        }

        // Drop any stored photo item separately when the frame is broken.
        public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer? byPlayer, float dropQuantityMultiplier = 1f)
        {
            BlockEntityFrame? be = world.BlockAccessor.GetBlockEntity(pos) as BlockEntityFrame;
            if (be != null && be.Inventory != null && !be.Inventory[0].Empty)
            {
                ItemStack stored = be.Inventory[0].Itemstack.Clone();
                world.SpawnItemEntity(stored, pos.ToVec3d().Add(0.5, 0.5, 0.5));
                be.Inventory[0].TakeOutWhole();
            }
            base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
        }

        public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
        {
            string baseInfo = base.GetPlacedBlockInfo(world, pos, forPlayer);

            BlockEntityFrame? be = world.BlockAccessor.GetBlockEntity(pos) as BlockEntityFrame;
            if (be == null || be.Inventory == null || be.Inventory[0].Empty) return baseInfo;

            ItemStack stack = be.Inventory[0].Itemstack;
            string caption = stack.Attributes?.GetString(PhotographAttrs.Caption) ?? string.Empty;
            string label = string.IsNullOrEmpty(caption) ? "Photograph" : caption;
            string line = $"Displaying: {label}";
            return string.IsNullOrEmpty(baseInfo) ? line : baseInfo + "\n" + line;
        }

        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
        {
            Item? sample = world.GetItem(new AssetLocation("phototesting", "photoplate"));

            var helps = new System.Collections.Generic.List<WorldInteraction>();
            if (sample != null)
            {
                helps.Add(new WorldInteraction
                {
                    ActionLangCode = "phototesting:heldhelp-frame-insert",
                    MouseButton = EnumMouseButton.Right,
                    Itemstacks = new[] { new ItemStack(sample) }
                });
            }
            helps.Add(new WorldInteraction
            {
                ActionLangCode = "phototesting:heldhelp-frame-take",
                MouseButton = EnumMouseButton.Right
            });
            return helps.ToArray();
        }
    }
}
