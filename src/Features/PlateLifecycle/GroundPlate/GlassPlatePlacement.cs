using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Phototesting.PlateLifecycle.GroundPlate
{
    internal static class GlassPlatePlacement
    {
        // Places the plate item as the matching state block on the ground and preserves process metadata.
        internal static void HandlePlacement(ICoreAPI api, ItemSlot slot, BlockSelection blockSel, string defaultPlateBlockState)
        {
            if (api?.World == null || slot?.Itemstack == null || blockSel?.Position == null) return;

            ItemStack stack = slot.Itemstack!;

            string plateBlockState = stack.Attributes.GetString("plateBlockState", defaultPlateBlockState);
            plateBlockState = plateBlockState switch
            {
                "clean" => "clean",
                "coated" => "coated",
                _ => "rough"
            };

            BlockPos placePos = blockSel.Position.UpCopy();
            IWorldAccessor world = api.World;

            Block plateBlock = world.GetBlock(new AssetLocation("phototesting", $"plate-{plateBlockState}"));
            if (plateBlock == null) return;

            Block existing = world.BlockAccessor.GetBlock(placePos);
            if (existing.Id != 0 && !existing.IsReplacableBy(plateBlock))
            {
                return;
            }

            world.BlockAccessor.SetBlock(plateBlock.Id, placePos);

            if (world.BlockAccessor.GetBlockEntity(placePos) is BlockEntityPlateProcessState stateBe)
            {
                string processId = PlateStateService.GetProcessId(stack);
                int stepIndex = PlateStateService.GetSensitizationStepIndex(stack);
                if (!string.IsNullOrWhiteSpace(processId) && stepIndex >= 0)
                {
                    stateBe.SetProcessState(processId, stepIndex);

                    double dryFinishTotalHours = PlateStateService.GetDryFinishTotalHours(stack);
                    if (dryFinishTotalHours > world.Calendar.TotalHours)
                    {
                        stateBe.RestoreDryWait(dryFinishTotalHours, world.Calendar.TotalHours);
                    }

                    world.BlockAccessor.MarkBlockEntityDirty(placePos);
                }
            }

            slot.TakeOut(1);
            slot.MarkDirty();
        }
    }
}
