using Vintagestory.API.Common;

namespace Phototesting.PlateLifecycle
{
    /// <summary>
    /// Tracks development-pipeline progress attributes on plate stacks and resolves the
    /// next development step for the currently stamped process.
    /// </summary>
    public static class PlateDevelopmentService
    {
        private const string DevelopmentStepIndexKey = "phototestingDevelopmentStepIndex";
        private const string DevelopmentStepApplicationsKey = "phototestingDevelopmentStepApplications";

        /// <summary>
        /// Returns the zero-based index of the last completed development step.
        /// Returns -1 when no development step has completed yet.
        /// </summary>
        public static int GetDevelopmentStepIndex(ItemStack? stack)
        {
            int idx = stack?.Attributes?.GetInt(DevelopmentStepIndexKey, -1) ?? -1;
            return idx < -1 ? -1 : idx;
        }

        /// <summary>
        /// Writes the zero-based index of the last completed development step.
        /// </summary>
        public static void SetDevelopmentStepIndex(ItemStack stack, int stepIndex)
        {
            stack.Attributes.SetInt(DevelopmentStepIndexKey, stepIndex < -1 ? -1 : stepIndex);
        }

        /// <summary>
        /// Returns how many applications have been completed for the in-progress step.
        /// </summary>
        public static int GetCurrentStepApplications(ItemStack? stack)
        {
            int count = stack?.Attributes?.GetInt(DevelopmentStepApplicationsKey, 0) ?? 0;
            return count < 0 ? 0 : count;
        }

        /// <summary>
        /// Writes how many applications have been completed for the in-progress step.
        /// </summary>
        public static void SetCurrentStepApplications(ItemStack stack, int applications)
        {
            stack.Attributes.SetInt(DevelopmentStepApplicationsKey, applications < 0 ? 0 : applications);
        }

        /// <summary>
        /// Clears development progress back to the pre-development state.
        /// </summary>
        public static void ResetDevelopmentProgress(ItemStack stack)
        {
            SetDevelopmentStepIndex(stack, -1);
            SetCurrentStepApplications(stack, 0);
        }

        /// <summary>
        /// Resolves the next development step for the plate's active process.
        /// Returns false when the pipeline is complete.
        /// </summary>
        public static bool TryResolveNextStep(ProcessRegistry registry, ItemStack plate, out DevelopmentStep step, out int nextStepIndex)
        {
            var process = registry.ResolveOrDefault(PlateStateService.GetProcessId(plate));
            int completedStepIndex = GetDevelopmentStepIndex(plate);
            nextStepIndex = completedStepIndex + 1;

            if (nextStepIndex < 0 || nextStepIndex >= process.DevelopmentPipeline.Count)
            {
                step = default!;
                return false;
            }

            step = process.DevelopmentPipeline[nextStepIndex];
            return true;
        }
    }
}

