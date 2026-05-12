using Vintagestory.API.Common;

namespace Phototesting.PlateLifecycle
{
    // Resolves per-plate process metadata into a normalized tray development spec.
    // Kept separate from block interaction flow so process expansion remains reusable and testable.
    internal static class TrayDevelopmentSpecResolver
    {
        // Uses the active plate process from mod state, falling back to the registry default.
        internal static PhotographyProcessDefinition ResolveProcessDefinition(ICoreAPI? api, ItemStack? plate)
        {
            return ProcessRegistryLookup.ResolveProcessOrDefault(api, PlateStateService.GetProcessId(plate));
        }

        // Expands the active process into tray-specific developer/fixer/water requirements.
        internal static TrayDevelopmentSpec ResolveDevelopmentSpec(ICoreAPI? api, ItemStack? plate, int waterUnitsPerUse, AssetLocation developerPortionCode, AssetLocation fixerPortionCode, AssetLocation waterPortionCode)
        {
            PhotographyProcessDefinition process = ResolveProcessDefinition(api, plate);

            DevelopmentStep? developerStep = TryGetStepForAction(process.DevelopmentPipeline, DevelopmentActionKind.Developer, out int developerStepIndex)
                ? process.DevelopmentPipeline[developerStepIndex]
                : null;
            DevelopmentStep? fixerStep = TryGetStepForAction(process.DevelopmentPipeline, DevelopmentActionKind.Fixer, out int fixerStepIndex)
                ? process.DevelopmentPipeline[fixerStepIndex]
                : null;
            bool hasWaterStep = TryGetStepForAction(process.DevelopmentPipeline, DevelopmentActionKind.Water, out int waterStepIndex);
            DevelopmentStep? waterStep = hasWaterStep ? process.DevelopmentPipeline[waterStepIndex] : null;

            AssetLocation devCode = ResolveRequiredPortionCode(developerStep, developerPortionCode);
            int devAmount = developerStep?.RequiredAmount ?? 1;
            int devApps = developerStep?.RequiredApplications ?? 1;

            AssetLocation fixCode = ResolveRequiredPortionCode(fixerStep, fixerPortionCode);
            int fixAmount = fixerStep?.RequiredAmount ?? 1;
            int fixApps = fixerStep?.RequiredApplications ?? 1;

            AssetLocation waterCode = ResolveRequiredPortionCode(waterStep, waterPortionCode);
            int waterAmount = waterStep?.RequiredAmount ?? waterUnitsPerUse;
            if (!hasWaterStep)
            {
                waterStepIndex = int.MaxValue;
            }

            return new TrayDevelopmentSpec(
                wetDurationMultiplier: process.WetDurationMultiplier,
                developerPortionCode: devCode,
                developerAmountPerUse: devAmount,
                developerApplicationsRequired: devApps,
                developerStepIndex: developerStepIndex,
                fixerPortionCode: fixCode,
                fixerAmountPerUse: fixAmount,
                fixerApplicationsRequired: fixApps,
                fixerStepIndex: fixerStepIndex,
                hasWaterRinseStep: hasWaterStep,
                waterPortionCode: waterCode,
                waterAmountPerUse: waterAmount,
                waterStepIndex: waterStepIndex);
        }

        // Uses a step-specific portion code when present.
        private static AssetLocation ResolveRequiredPortionCode(DevelopmentStep? step, AssetLocation fallback)
        {
            if (step == null || string.IsNullOrWhiteSpace(step.RequiredPortionCode)) return fallback;
            return new AssetLocation(step.RequiredPortionCode);
        }

        // Finds the first development-pipeline step for a given tray action kind.
        private static bool TryGetStepForAction(IReadOnlyList<DevelopmentStep>? steps, DevelopmentActionKind action, out int stepIndex)
        {
            stepIndex = -1;
            if (steps == null) return false;

            for (int i = 0; i < steps.Count; i++)
            {
                if (steps[i].ActionKind != action) continue;
                stepIndex = i;
                return true;
            }

            return false;
        }
    }

    // Immutable per-process tray recipe expansion used by tray interaction code so it can work with normalized developer/fixer/water requirements.
    internal readonly struct TrayDevelopmentSpec
    {
        public double WetDurationMultiplier { get; }

        public AssetLocation DeveloperPortionCode { get; }
        public int DeveloperAmountPerUse { get; }
        public int DeveloperApplicationsRequired { get; }
        public int DeveloperStepIndex { get; }

        public AssetLocation FixerPortionCode { get; }
        public int FixerAmountPerUse { get; }
        public int FixerApplicationsRequired { get; }
        public int FixerStepIndex { get; }

        public bool HasWaterRinseStep { get; }
        public AssetLocation WaterPortionCode { get; }
        public int WaterAmountPerUse { get; }
        public int WaterStepIndex { get; }

        // Normalizes counts to at least one use/application while preserving the resolved process step indexes.
        public TrayDevelopmentSpec(
            double wetDurationMultiplier,
            AssetLocation developerPortionCode,
            int developerAmountPerUse,
            int developerApplicationsRequired,
            int developerStepIndex,
            AssetLocation fixerPortionCode,
            int fixerAmountPerUse,
            int fixerApplicationsRequired,
            int fixerStepIndex,
            bool hasWaterRinseStep,
            AssetLocation waterPortionCode,
            int waterAmountPerUse,
            int waterStepIndex)
        {
            WetDurationMultiplier = wetDurationMultiplier;

            DeveloperPortionCode = developerPortionCode;
            DeveloperAmountPerUse = developerAmountPerUse < 1 ? 1 : developerAmountPerUse;
            DeveloperApplicationsRequired = developerApplicationsRequired < 1 ? 1 : developerApplicationsRequired;
            DeveloperStepIndex = developerStepIndex;

            FixerPortionCode = fixerPortionCode;
            FixerAmountPerUse = fixerAmountPerUse < 1 ? 1 : fixerAmountPerUse;
            FixerApplicationsRequired = fixerApplicationsRequired < 1 ? 1 : fixerApplicationsRequired;
            FixerStepIndex = fixerStepIndex;

            HasWaterRinseStep = hasWaterRinseStep;
            WaterPortionCode = waterPortionCode;
            WaterAmountPerUse = waterAmountPerUse < 1 ? 1 : waterAmountPerUse;
            WaterStepIndex = waterStepIndex;
        }
    }

    // Resolves action kind from held chemicals and validates that timed actions are still process-valid.
    internal static class TrayActionResolver
    {
        // Returns the first compatible tray action for the currently held chemical.
        internal static bool TryResolveHeldChemicalAction(ItemSlot? activeSlot, in TrayDevelopmentSpec spec, out TrayActionKind actionKind)
        {
            if (IsHoldingChemical(activeSlot, spec.DeveloperPortionCode))
            {
                actionKind = TrayActionKind.Developer;
                return true;
            }

            if (IsHoldingChemical(activeSlot, spec.FixerPortionCode))
            {
                actionKind = TrayActionKind.Fixer;
                return true;
            }

            if (spec.HasWaterRinseStep && IsHoldingChemical(activeSlot, spec.WaterPortionCode))
            {
                actionKind = TrayActionKind.Water;
                return true;
            }

            actionKind = default;
            return false;
        }

        // Ensures a persisted timed action still matches the active process requirements.
        internal static bool IsTimedActionAllowed(TrayActionKind actionKind, in TrayDevelopmentSpec spec)
        {
            return actionKind != TrayActionKind.Water || spec.HasWaterRinseStep;
        }

        private static bool IsHoldingChemical(ItemSlot? slot, AssetLocation code)
        {
            return slot?.Itemstack != null && WetPlateChemicalUtil.IsChemicalOrContainerWith(slot.Itemstack, code);
        }
    }
}

