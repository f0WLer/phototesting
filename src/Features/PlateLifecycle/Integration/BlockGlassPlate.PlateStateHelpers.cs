using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Phototesting.PlateLifecycle.Blocks
{
    public sealed partial class BlockGlassPlate
    {
        // Reads the configured polish duration and clamps it to a safe interaction window.
        private float GetPolishSeconds()
        {
            float seconds = PlateCfg?.PolishSeconds ?? DefaultPolishSeconds;
            if (seconds < 0f) seconds = 0f;
            if (seconds > 30f) seconds = 30f;
            return seconds;
        }

        // Reads the configured pour duration used by chemical sensitization steps on placed plates.
        private float GetSensitizationPourSeconds()
        {
            float seconds = PlateCfg?.SensitizationPourSeconds ?? 1.5f;
            if (seconds < 0f) seconds = 0f;
            if (seconds > 30f) seconds = 30f;
            return seconds;
        }

        // Reads the configured dry-wait duration for sensitization steps that do not consume an item.
        private float GetSensitizationDrySeconds()
        {
            float seconds = PlateCfg?.SensitizationDrySeconds ?? 15f;
            if (seconds < 0f) seconds = 0f;
            if (seconds > 300f) seconds = 300f;
            return seconds;
        }

        // Resolves how many plain cloth items polishing should consume after config fallbacks are applied.
        private int GetPlainClothConsumeCount()
        {
            if (PlateCfg?.ConsumePlainClothOnPolish != true) return 0;

            int amount = PlateCfg.PlainClothConsumedPerPolish;
            if (amount < 0) amount = 0;
            return amount;
        }

        // Normalizes the current placed-block variant into the logical plate state used by pickups and process routing.
        private string GetPlateState()
        {
            string? variantState = Variant?["state"];
            if (!string.IsNullOrEmpty(variantState)) return variantState;

            string path = Code?.Path ?? "";
            if (path.EndsWith("-clean")) return "clean";
            if (path.EndsWith("-coated")) return "coated";
            return "rough";
        }

        // Resolves the matching placed block for a target visual state such as rough, clean, or coated.
        private Block? GetBlockForState(IWorldAccessor world, string state)
        {
            return world?.GetBlock(new AssetLocation(Code?.Domain ?? "phototesting", $"plate-{state}"));
        }

        // Builds the pickup item stack that mirrors the placed plate's current state and any in-progress process metadata.
        private bool TryCreatePlateItemStack(IWorldAccessor world, BlockPos pos, out ItemStack stack)
        {
            stack = default!;

            string state = GetPlateState();
            Item? item = world?.GetItem(_glassPlateItemCode);
            if (item == null) return false;

            stack = new ItemStack(item);

            PlateStage stage = state switch
            {
                "clean" => PlateStage.Clean,
                "coated" => PlateStage.Sensitizing,
                _ => PlateStage.Rough
            };

            PlateStateService.SetStage(stack, stage);
            stack.Attributes.SetString("plateBlockState", state);

            // Preserve process lock/progress when picking up an in-progress placed plate.
            if (world != null && TryGetPlacedPlateProcessState(world, pos, out string processId, out int stepIndex))
            {
                PlateStateService.SetProcessId(stack, processId);
                PlateStateService.SetSensitizationStepIndex(stack, stepIndex);

                // If a block-side dry wait is still active, carry its absolute deadline onto the
                // item so the dry step can be enforced before the next chemical is applied.
                if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityPlateProcessState be && be.IsDryWaitActive)
                {
                    PlateStateService.SetDryFinishTotalHours(stack, be.DryFinishTotalHours);
                }
            }

            if (stage == PlateStage.Rough)
            {
                PlateStateService.SetNameLangCode(stack, "phototesting:plate-name-glass");
            }
            else if (stage == PlateStage.Clean)
            {
                PlateStateService.SetNameLangCode(stack, "phototesting:plate-name-glass-clean");
            }

            return true;
        }
    }
}

