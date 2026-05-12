using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Phototesting.AdminTooling;

namespace Phototesting.PlateLifecycle.Blocks
{
    // Placed glass plate block behavior and top-level interaction routing.
    // Starts from block interaction or placement code and owns shared config lookups for ground plates.
    // Delegates sensitization step rules and stage mutation to plate lifecycle chemistry services.
    // Side: mixed client/server block owner, but authoritative state changes must still route through world-side logic.
    // Related files: BlockGlassPlate.Interaction.cs, Features/PlateLifecycle/Chemistry/BlockGlassPlate.PlateLifecycleChemistry.cs, Features/PlateLifecycle/Chemistry/PlateSensitizationService.cs.
    public sealed partial class BlockGlassPlate : Block
    {
        private const float DefaultPolishSeconds = 2.0f;
        private static readonly AssetLocation _glassPlateItemCode = new("phototesting", "glassplate");

        private PhotoTestingConfig? Cfg => PhotoTestingConfigAccess.ResolveConfig(api);
        private PlateProcessingConfig? PlateCfg => Cfg?.PlateProcessing;
        private static readonly AssetLocation _plainClothCode = new("game", "cloth-plain");
        private static readonly AssetLocation _polishSound = new("game:sounds/player/chalkdraw");
        private static readonly AssetLocation _phototestingPourSound = new("game:sounds/effect/water-fill");

        // Forces placed plates into the transparent chunk pass so their alpha-textured geometry renders correctly.
        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);

            // These blocks use per-texture alpha. If rendered in the opaque pass, they will write depth
            // and can cause "see under terrain" artifacts because terrain behind them never renders.
            RenderPass = EnumChunkRenderPass.Transparent;
        }

        // Drops the logical plate item instead of the raw placed block so process state survives block breakage.
        public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
        {
            // Plates should drop their corresponding *item* (rough/clean/coated), not the block itself.
            if (TryCreatePlateItemStack(world, pos, out ItemStack stack)) return new[] { stack };

            return base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier);
        }

        // Gives middle-click and pick-block the same state-preserving plate item used for normal drops.
        public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
        {
            if (TryCreatePlateItemStack(world, pos, out ItemStack stack)) return stack;

            return base.OnPickBlock(world, pos);
        }

    }
}


