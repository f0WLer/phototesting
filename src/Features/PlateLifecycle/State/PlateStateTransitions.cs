using Vintagestory.API.Common;

namespace Phototesting.PlateLifecycle
{
    /// <summary>
    /// Multi-step plate state transitions that compose several <see cref="PlateStateService"/> writes.
    /// Single-call wrappers live on <see cref="PlateStateService"/> directly.
    /// </summary>
    internal static class PlateStateTransitions
    {
        internal static bool IsDevelopingFamily(ItemStack? plate)
        {
            PlateStage stage = PlateStateService.GetStage(plate);
            return stage == PlateStage.Developing || stage == PlateStage.Developed;
        }

        internal static bool IsWetStage(ItemStack? plate)
        {
            PlateStage stage = PlateStateService.GetStage(plate);
            return stage == PlateStage.Sensitized
                || stage == PlateStage.Exposed
                || stage == PlateStage.Developing
                || stage == PlateStage.Developed;
        }

        internal static void SetStageAndName(ItemStack plate, PlateStage stage, string? nameLangCode)
        {
            PlateStateService.SetStage(plate, stage);
            PlateStateService.SetNameLangCode(plate, nameLangCode);
        }

        internal static void ApplyProcessProgress(ItemStack plate, string processId, int sensitizationStepIndex)
        {
            PlateStateService.SetProcessId(plate, processId);
            PlateStateService.SetSensitizationStepIndex(plate, sensitizationStepIndex);
        }

        internal static bool TryAdvanceSensitizationStep(
            ItemStack plate,
            PhotographyProcessDefinition process,
            int nextStepIndex,
            out bool complete)
        {
            complete = false;
            if (nextStepIndex < 0 || nextStepIndex >= process.SensitizationSteps.Count) return false;

            if (!PlateStateService.IsProcessLocked(plate))
            {
                PlateStateService.SetProcessId(plate, process.Id);
            }

            PlateStateService.SetSensitizationStepIndex(plate, nextStepIndex);
            SensitizationStep step = process.SensitizationSteps[nextStepIndex];
            PlateStateService.SetNameLangCode(plate, step.ResultPlateNameLangCode);
            complete = PlateStateService.IsSensitizationComplete(plate, process);

            if (!complete)
            {
                PlateStateService.SetStage(plate, PlateStage.Sensitizing);
            }

            return true;
        }

        internal static void InitializeSensitizedPlate(IWorldAccessor world, ItemStack plate, PhotographyProcessDefinition process)
        {
            PlateStateService.SetProcessId(plate, process.Id);
            PlateStateService.SetStage(plate, PlateStage.Sensitized);
            PlateStateService.SetNameLangCode(plate, process.SensitizedPlateNameLangCode);

            double effectiveHours = PlateDryingTransition.ResolveWetDurationHours(world.Api) * process.WetDurationMultiplier;
            PlateDryingTransition.ResetTimer(world, plate, effectiveHours);
        }

        internal static void TransitionToRough(ItemStack plate, string roughNameLangCode)
        {
            PlateStateService.SetStage(plate, PlateStage.Rough);
            PlateStateService.SetNameLangCode(plate, roughNameLangCode);
        }

        internal static void ResetWetTimerForMultiplier(ICoreAPI? api, IWorldAccessor world, ItemStack plate, double wetDurationMultiplier)
        {
            double effectiveHours = PlateDryingTransition.ResolveWetDurationHours(api) * wetDurationMultiplier;
            PlateDryingTransition.ResetTimer(world, plate, effectiveHours);
        }
    }
}
