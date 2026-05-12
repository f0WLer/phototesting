using Phototesting.AdminTooling;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Phototesting.PlateLifecycle.Tray
{
    // PlateLifecycle ownership: tray-as-owner block contract and lifecycle integration.
    public sealed partial class BlockDevelopmentTray : Block
    {
        // Liquid portion itemsPerLitre is 100 in our json.
        private const int DefaultChemicalUnitsPerUse = 40;

        internal const string ActionDeveloper = "developer";
        internal const string ActionFixer = "fixer";
        internal const string ActionWater = "water";

        private PhotoTestingConfig? Cfg => PhotoTestingConfigAccess.ResolveConfig(api);

        private static readonly AssetLocation _sensitizedPlateItemCode = new("phototesting", "sensitizedplate");
        private static readonly AssetLocation _photoPlateItemCode = new("phototesting", "photoplate");

        private static readonly AssetLocation _developerPortionCode = new("phototesting", "developerportion");
        private static readonly AssetLocation _fixerPortionCode = new("phototesting", "fixerportion");
        private static readonly AssetLocation _waterPortionCode = new("game", "waterportion");
        private static readonly AssetLocation _glassPlateItemCode = new("phototesting", "glassplate");
        private static readonly AssetLocation _chemicalPourSound = new("game:sounds/effect/water-fill");
        private static readonly AssetLocation _fizzSound = new("phototesting", "sounds/fizz");

        // Keeps tray startup local to the owner partial in case future initialization needs to sit beside the shared constants.
        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
        }

        // Captures the placer-facing so inserted plate and photo meshes orient correctly from the moment the tray appears.
        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
        {
            bool placed = base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);
            if (!placed) return false;

            if (world == null || blockSel?.Position == null) return true;

            BlockPos placedPos = ResolvePlacedPos(world, blockSel);
            if (world.BlockAccessor.GetBlockEntity(placedPos) is BlockEntityDevelopmentTray be)
            {
                BlockFacing playerFacing = BlockFacing.HorizontalFromYaw(byPlayer?.Entity?.SidedPos?.Yaw ?? 0f);
                be.SetPlacementFacing(playerFacing.Code, markBlockDirty: true);
            }

            return true;
        }

        // Resolves the final placed tray position.
        private BlockPos ResolvePlacedPos(IWorldAccessor world, BlockSelection blockSel)
        {
            BlockPos selectedPos = blockSel.Position;
            Block selectedBlock = world.BlockAccessor.GetBlock(selectedPos);
            if (selectedBlock != null && selectedBlock.IsReplacableBy(this))
            {
                return selectedPos;
            }

            return selectedPos.AddCopy(blockSel.Face);
        }

        // Shared tray interaction entry point that routes to client prediction or server authority depending on side.
        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (world == null || byPlayer == null || blockSel == null) return false;
            return HandlePlateLifecycleInteractionStart(world, byPlayer, blockSel);
        }

        // Drops the base tray block plus any inserted plate so stage-specific tray block variants do not leak into inventory.
        public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
        {
            // Always drop the base tray block (red/blue/fire), not a loaded variant.
            var drops = new List<ItemStack>();

            Block? baseTray = GetBaseTrayBlock(world);
            if (baseTray != null)
            {
                drops.Add(new ItemStack(baseTray));
            }
            else
            {
                drops.AddRange(base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier) ?? Array.Empty<ItemStack>());
            }

            if (world?.BlockAccessor?.GetBlockEntity(pos) is BlockEntityDevelopmentTray be && be.PlateStack != null)
            {
                drops.Add(be.PlateStack.Clone());
            }

            return drops.ToArray();
        }

        // Gets the plain base tray block for this variant.
        private Block? GetBaseTrayBlock(IWorldAccessor world)
        {
            if (world == null || Code == null) return null;

            string path = Code.Path;
            if (!path.StartsWith("developmenttray-")) return null;

            // path is one of:
            // developmenttray-red
            // developmenttray-red-exposed/developed/finished
            string rest = path.Substring("developmenttray-".Length);
            int dash = rest.IndexOf('-');
            string clay = dash >= 0 ? rest.Substring(0, dash) : rest;

            AssetLocation baseLoc = new(Code.Domain, $"developmenttray-{clay}");
            return world.GetBlock(baseLoc);
        }

        // Gets developer pour duration.
        private float GetDeveloperPourSeconds()
        {
            return TrayDurationProvider.GetDurationSeconds(Cfg, TrayActionKind.Developer);
        }

        // Gets fixer pour duration.
        private float GetFixerPourSeconds()
        {
            return TrayDurationProvider.GetDurationSeconds(Cfg, TrayActionKind.Fixer);
        }

        // Gets chemical units per use.
        private int GetChemicalUnitsPerUse()
        {
            return TrayDurationProvider.GetChemicalUnitsPerUse(Cfg, DefaultChemicalUnitsPerUse);
        }
        // PlateLifecycle seam: routes tray interaction start through a single entry orchestration point.
        private bool HandlePlateLifecycleInteractionStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            return HandleWorldObjectInteractionStart(world, byPlayer, blockSel);
        }

    }
}
