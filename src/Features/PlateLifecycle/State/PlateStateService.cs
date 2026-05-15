using Vintagestory.API.Common;

namespace Phototesting.PlateLifecycle
{
    /// <summary>
    /// Reads and writes per-plate state attributes (process ID and lifecycle stage) on an
    /// <see cref="ItemStack"/>, including legacy-fallback behavior for pre-multi-process plates.
    /// </summary>
    public static class PlateStateService
    {
        /// <summary>
        /// Returns the process ID stored on <paramref name="stack"/>, or
        /// <see cref="PlateStateAttributes.DefaultProcessId"/> when the attribute is absent —
        /// preserving behavior for legacy plates.
        /// </summary>
        public static string GetProcessId(ItemStack? stack)
        {
            string? stored = stack?.Attributes?.GetString(PlateStateAttributes.ProcessId);
            return string.IsNullOrEmpty(stored) ? PlateStateAttributes.DefaultProcessId : stored!;
        }

        /// <summary>
        /// Ensures a concrete process ID exists on <paramref name="stack"/>.
        /// Legacy stacks with no process attribute are upgraded to the default process ID.
        /// Returns the process ID that is (or was already) on the stack.
        /// </summary>
        public static string EnsureProcessId(ItemStack stack)
        {
            string? stored = stack.Attributes.GetString(PlateStateAttributes.ProcessId);
            if (string.IsNullOrEmpty(stored))
            {
                stack.Attributes.SetString(PlateStateAttributes.ProcessId, PlateStateAttributes.DefaultProcessId);
                return PlateStateAttributes.DefaultProcessId;
            }

            return stored!;
        }

        /// <summary>Writes the process ID attribute on <paramref name="stack"/>.</summary>
        public static void SetProcessId(ItemStack stack, string processId)
        {
            stack.Attributes.SetString(PlateStateAttributes.ProcessId, processId);
        }

        /// <summary>
        /// True when a concrete processId attribute has been stamped on the stack.
        /// Legacy stacks with no processId are considered unlocked.
        /// </summary>
        public static bool IsProcessLocked(ItemStack? stack)
        {
            string? stored = stack?.Attributes?.GetString(PlateStateAttributes.ProcessId);
            return !string.IsNullOrEmpty(stored);
        }

        /// <summary>
        /// Returns the zero-based index of the last completed sensitization step.
        /// Returns -1 when no step has been completed yet.
        /// </summary>
        public static int GetSensitizationStepIndex(ItemStack? stack)
        {
            int idx = stack?.Attributes?.GetInt(PlateStateAttributes.SensitizationStepIndex, -1) ?? -1;
            return idx < -1 ? -1 : idx;
        }

        /// <summary>Writes the zero-based index of the last completed sensitization step.</summary>
        public static void SetSensitizationStepIndex(ItemStack stack, int stepIndex)
        {
            stack.Attributes.SetInt(PlateStateAttributes.SensitizationStepIndex, stepIndex < -1 ? -1 : stepIndex);
        }

        /// <summary>Returns the optional explicit name language key for this plate.</summary>
        public static string? GetNameLangCode(ItemStack? stack)
        {
            string? key = stack?.Attributes?.GetString(PlateStateAttributes.NameLangCode);
            return string.IsNullOrWhiteSpace(key) ? null : key;
        }

        /// <summary>Writes an explicit name language key for this plate.</summary>
        public static void SetNameLangCode(ItemStack stack, string? nameLangCode)
        {
            if (string.IsNullOrWhiteSpace(nameLangCode))
            {
                stack.Attributes.RemoveAttribute(PlateStateAttributes.NameLangCode);
                return;
            }

            stack.Attributes.SetString(PlateStateAttributes.NameLangCode, nameLangCode);
        }

        /// <summary>
        /// Returns true when the plate has completed all sensitization steps defined by the process.
        /// </summary>
        public static bool IsSensitizationComplete(ItemStack? stack, PhotographyProcessDefinition process)
        {
            if (process.SensitizationSteps.Count <= 0) return true;
            return GetSensitizationStepIndex(stack) >= process.SensitizationSteps.Count - 1;
        }

        /// <summary>
        /// Returns the current <see cref="PlateStage"/> for <paramref name="stack"/>.
        /// Returns <see cref="PlateStage.Unknown"/> when the stage attribute is absent or unrecognized.
        /// </summary>
        public static PlateStage GetStage(ItemStack? stack)
        {
            string? raw = stack?.Attributes?.GetString(PlateStateAttributes.Stage);
            return PlateStageUtil.FromAttributeString(raw);
        }

        /// <summary>
        /// Ensures a valid stage exists on <paramref name="stack"/>; if missing/unknown,
        /// writes <paramref name="fallbackStage"/>.
        /// </summary>
        public static PlateStage EnsureStage(ItemStack stack, PlateStage fallbackStage)
        {
            PlateStage stage = GetStage(stack);
            if (stage == PlateStage.Unknown)
            {
                SetStage(stack, fallbackStage);
                return fallbackStage;
            }

            return stage;
        }

        /// <summary>Writes the lifecycle stage attribute on <paramref name="stack"/>.</summary>
        public static void SetStage(ItemStack stack, PlateStage stage)
        {
            stack.Attributes.SetString(PlateStateAttributes.Stage, PlateStageUtil.ToAttributeString(stage));
        }

        /// <summary>
        /// Returns true when the plate is in the exposed lifecycle stage.
        /// </summary>
        public static bool IsPlateExposed(ItemStack? stack)
        {
            return GetStage(stack) == PlateStage.Exposed;
        }

        /// <summary>
        /// Returns the absolute Calendar.TotalHours deadline at which the in-progress block-side
        /// air-dry completes, or a negative value when no dry wait is active.
        /// </summary>
        public static double GetDryFinishTotalHours(ItemStack? stack)
        {
            return stack?.Attributes?.GetDouble(PlateStateAttributes.DryFinishTotalHours, -1.0) ?? -1.0;
        }

        /// <summary>
        /// Stores the absolute Calendar.TotalHours dry-wait deadline on the item.
        /// Pass a negative value (or call <see cref="ClearDryFinishTotalHours"/>) to remove it.
        /// </summary>
        public static void SetDryFinishTotalHours(ItemStack stack, double totalHours)
        {
            if (totalHours <= 0.0)
            {
                stack.Attributes.RemoveAttribute(PlateStateAttributes.DryFinishTotalHours);
                return;
            }

            stack.Attributes.SetDouble(PlateStateAttributes.DryFinishTotalHours, totalHours);
        }

        /// <summary>Removes the dry-wait deadline attribute from <paramref name="stack"/>.</summary>
        public static void ClearDryFinishTotalHours(ItemStack stack)
        {
            stack.Attributes.RemoveAttribute(PlateStateAttributes.DryFinishTotalHours);
        }

        /// <summary>
        /// True when the item carries an active dry-wait deadline that has not yet elapsed.
        /// </summary>
        public static bool IsDryWaitActive(ItemStack? stack, double currentTotalHours)
        {
            double finish = GetDryFinishTotalHours(stack);
            return finish > 0.0 && currentTotalHours < finish;
        }
    }
}

