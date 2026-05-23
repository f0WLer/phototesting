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

        /// <summary>
        /// Absolute in-game Calendar.TotalHours at which the in-progress block-side air-dry step
        /// completes. Copied from the block entity onto the item when the plate is picked up mid-dry
        /// so the dry wait can continue in inventory (or be enforced before the next step).
        /// Absent when no dry wait is active.
        /// </summary>
        public const string DryFinishTotalHours = "phototestingDryFinishTotalHours";

        /// <summary>
        /// Stable identifier for the plate's in-progress accumulation session.
        /// Set when exposure starts; cleared when the plate transitions to Exposed.
        /// Used by the client to locate the matching accumulation buffer in the registry.
        /// </summary>
        public const string ExposureId = "phototestingExposureId";

        /// <summary>Frames accumulated so far in the current or most recent exposure session.</summary>
        public const string ExposedFrames = "phototestingExposedFrames";

        /// <summary>Target frame count for a correct exposure (from the plate's process profile).</summary>
        public const string ExposureTargetFrames = "phototestingExposureTargetFrames";
    }
}

