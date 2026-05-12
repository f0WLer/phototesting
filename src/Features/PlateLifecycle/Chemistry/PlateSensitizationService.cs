using Vintagestory.API.Common;

namespace Phototesting.PlateLifecycle
{
    /// <summary>
    /// Shared execution helpers for process-driven plate sensitization.
    /// Keeps process resolution and step/state transitions in one place so
    /// item and block interaction paths can reuse the same rules.
    /// </summary>
    internal static class PlateSensitizationService
    {
        private static readonly AssetLocation _sensitizedPlateItemCode = new("phototesting", "sensitizedplate");

        internal static bool TryResolveNextStep(
            ProcessRegistry registry,
            ItemStack plate,
            ItemSlot? chemicalSlot,
            out PhotographyProcessDefinition process,
            out SensitizationStep step,
            out int nextStepIndex)
        {
            process = ProcessRegistry.DefaultProcess;
            step = process.SensitizationSteps.Count > 0
                ? process.SensitizationSteps[0]
                : new SensitizationStep("", null, 0, "");
            nextStepIndex = -1;

            if (PlateStateService.IsProcessLocked(plate))
            {
                process = registry.ResolveOrDefault(PlateStateService.GetProcessId(plate));
                nextStepIndex = PlateStateService.GetSensitizationStepIndex(plate) + 1;
                if (nextStepIndex < 0 || nextStepIndex >= process.SensitizationSteps.Count) return false;

                step = process.SensitizationSteps[nextStepIndex];
                if (step.Kind == SensitizationStepKind.Dry)
                {
                    if (TryResolveOptionalDrySkip(process, chemicalSlot, nextStepIndex + 1, out SensitizationStep skippedStep, out int skippedStepIndex))
                    {
                        step = skippedStep;
                        nextStepIndex = skippedStepIndex;
                    }

                    return true;
                }

                if (string.IsNullOrWhiteSpace(step.RequiredPortionCode)) return false;
                if (chemicalSlot == null) return false;

                var requiredCode = new AssetLocation(step.RequiredPortionCode);
                return WetPlateChemicalUtil.HasConsumableChemical(chemicalSlot, requiredCode, step.RequiredAmount);
            }

            foreach (var candidateProcess in registry.AllProcesses.Values)
            {
                if (candidateProcess.SensitizationSteps.Count <= 0) continue;

                SensitizationStep firstStep = candidateProcess.SensitizationSteps[0];
                if (firstStep.Kind == SensitizationStepKind.Dry) continue;
                if (string.IsNullOrWhiteSpace(firstStep.RequiredPortionCode)) continue;
                if (chemicalSlot == null) continue;

                var requiredCode = new AssetLocation(firstStep.RequiredPortionCode);
                if (!WetPlateChemicalUtil.HasConsumableChemical(chemicalSlot, requiredCode, firstStep.RequiredAmount))
                    continue;

                process = candidateProcess;
                nextStepIndex = 0;
                step = firstStep;
                return true;
            }

            return false;
        }

        // Allows dry steps to be optional when player is already holding the next matching chemical.
        private static bool TryResolveOptionalDrySkip(
            PhotographyProcessDefinition process,
            ItemSlot? chemicalSlot,
            int startIndex,
            out SensitizationStep step,
            out int stepIndex)
        {
            step = default!;
            stepIndex = -1;

            if (chemicalSlot?.Itemstack == null) return false;
            if (startIndex < 0 || startIndex >= process.SensitizationSteps.Count) return false;

            // Only skip contiguous dry waits; never skip required chemical steps.
            int candidateIndex = startIndex;
            while (candidateIndex < process.SensitizationSteps.Count && process.SensitizationSteps[candidateIndex].Kind == SensitizationStepKind.Dry)
            {
                candidateIndex++;
            }

            if (candidateIndex < 0 || candidateIndex >= process.SensitizationSteps.Count) return false;

            SensitizationStep candidate = process.SensitizationSteps[candidateIndex];
            if (candidate.Kind != SensitizationStepKind.Chemical) return false;
            if (string.IsNullOrWhiteSpace(candidate.RequiredPortionCode)) return false;

            var requiredCode = new AssetLocation(candidate.RequiredPortionCode);
            if (!WetPlateChemicalUtil.HasConsumableChemical(chemicalSlot, requiredCode, candidate.RequiredAmount))
            {
                return false;
            }

            step = candidate;
            stepIndex = candidateIndex;
            return true;
        }

        internal static bool TryConsumeChemicalStep(ItemSlot chemicalSlot, SensitizationStep step)
        {
            if (step.Kind != SensitizationStepKind.Chemical) return false;
            if (string.IsNullOrWhiteSpace(step.RequiredPortionCode)) return false;

            var requiredCode = new AssetLocation(step.RequiredPortionCode);
            return WetPlateChemicalUtil.TryConsumeChemical(chemicalSlot, requiredCode, step.RequiredAmount);
        }

        internal static bool TryGetChemicalStep(PhotographyProcessDefinition process, int stepIndex, out SensitizationStep step)
        {
            step = default!;
            if (process.SensitizationSteps.Count <= stepIndex) return false;

            SensitizationStep candidate = process.SensitizationSteps[stepIndex];
            if (candidate.Kind != SensitizationStepKind.Chemical) return false;
            if (string.IsNullOrWhiteSpace(candidate.RequiredPortionCode)) return false;

            step = candidate;
            return true;
        }

        internal static bool TryAdvancePlateState(ItemStack plate, PhotographyProcessDefinition process, int nextStepIndex, out bool complete)
        {
            return PlateLifecycleStateCoordinator.TryAdvanceSensitizationStep(plate, process, nextStepIndex, out complete);
        }

        internal static bool TryCreateSensitizedPlateStack(IWorldAccessor world, ItemStack sourcePlate, PhotographyProcessDefinition process, out ItemStack sensitizedPlate)
        {
            sensitizedPlate = default!;
            Item? sensitizedItem = world.GetItem(_sensitizedPlateItemCode);
            if (sensitizedItem == null) return false;

            sensitizedPlate = new ItemStack(sensitizedItem, 1);
            sensitizedPlate.Attributes.MergeTree(sourcePlate.Attributes.Clone());
            PlateLifecycleStateCoordinator.InitializeSensitizedPlate(world, sensitizedPlate, process);

            return true;
        }
    }
}

