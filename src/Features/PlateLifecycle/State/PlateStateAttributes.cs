namespace Phototesting.PlateLifecycle
{
    /// <summary>
    /// Canonical attribute-key and default-value constants for plate state stored on
    /// <see cref="Vintagestory.API.Common.ItemStack"/>. All plate attribute reads and writes
    /// should go through <see cref="PlateStateService"/>; refer to this class only for the
    /// raw key strings.
    /// </summary>
    public static class PlateStateAttributes
    {
        /// <summary>
        /// Identifies the photography process used for this plate (e.g. "iodide", "chloride", "bromide").
        /// Absent on plates created before multi-process support; legacy fallback is <see cref="DefaultProcessId"/>.
        /// </summary>
        public const string ProcessId = "phototestingProcessId";

        /// <summary>
        /// Fallback process ID written to legacy plates that carry no process attribute.
        /// Must match <see cref="ProcessRegistry.DefaultProcess"/>.Id.
        /// </summary>
        public const string DefaultProcessId = "iodide";

        /// <summary>
        /// Current lifecycle stage string. Canonical definition — use this over WetPlateAttrs.PlateStage.
        /// Values: "rough", "clean", "sensitizing", "sensitized", "exposed", "developing", "developed", "finished".
        /// </summary>
        public const string Stage = "phototestingPlateStage";

        /// <summary>
        /// Zero-based index of the last completed sensitization step for this plate.
        /// Absent means no sensitization steps have been completed yet.
        /// </summary>
        public const string SensitizationStepIndex = "phototestingSensitizationStep";

        /// <summary>
        /// Optional language-key override used to present process/step-specific plate names.
        /// When absent, name resolution falls back to stage/process defaults.
        /// </summary>
        public const string NameLangCode = "phototestingPlateNameLangCode";
    }
}

