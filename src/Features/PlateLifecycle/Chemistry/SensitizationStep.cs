namespace Phototesting.PlateLifecycle
{
    /// <summary>Discriminates the two kinds of sensitization step an item can perform.</summary>
    public enum SensitizationStepKind
    {
        /// <summary>Player holds a liquid portion in offhand; it is consumed on completion.</summary>
        Chemical,

        /// <summary>Player holds the plate for a timed duration with no item consumed - air-drying.</summary>
        Dry
    }

    /// <summary>
    /// One discrete step in the clean-to-sensitized pipeline for a photographic process.
    /// Steps are applied in order; all must complete before the plate becomes
    /// <see cref="PlateStage.Sensitized"/> and receives its process ID.
    /// </summary>
    public sealed class SensitizationStep
    {
        /// <summary>
        /// Unique identifier for this step within its process (e.g. "coat-collodion", "silver-bath").
        /// Stored on the plate as the current step index, not by string.
        /// </summary>
        public string StepId { get; }

        /// <summary>
        /// Item or liquid portion code required for this step, as a "domain:path" string
        /// (e.g. "phototesting:collodionportion"). Null when no held portion is consumed.
        /// Ignored for <see cref="SensitizationStepKind.Dry"/> steps.
        /// </summary>
        public string? RequiredPortionCode { get; }

        /// <summary>
        /// Units consumed per application, using the same unit system as WetPlateChemicalUtil.
        /// Ignored when <see cref="RequiredPortionCode"/> is null or <see cref="Kind"/> is <see cref="SensitizationStepKind.Dry"/>.
        /// </summary>
        public int RequiredAmount { get; }

        /// <summary>
        /// Block-variant stage label active while this step is in progress.
        /// Passed to SwapTrayBlockForPlateStage for intermediate visual state.
        /// </summary>
        public string IntermediateStageLabel { get; }

        /// <summary>
        /// Whether this step consumes a liquid chemical or is a passive air-dry wait.
        /// Defaults to <see cref="SensitizationStepKind.Chemical"/>.
        /// </summary>
        public SensitizationStepKind Kind { get; }

        /// <summary>
        /// Override hold duration in seconds for <see cref="SensitizationStepKind.Dry"/> steps.
        /// When null the global <c>PlateProcessingConfig.SensitizationDrySeconds</c> is used.
        /// Ignored for <see cref="SensitizationStepKind.Chemical"/> steps.
        /// </summary>
        public float? WaitSeconds { get; }

        /// <summary>
        /// Optional language key to use for plate naming after this step is completed.
        /// Example: "phototesting:plate-name-chloride-coated".
        /// </summary>
        public string? ResultPlateNameLangCode { get; }

        /// <summary>Convenience constructor for Chemical steps (existing call sites unchanged).</summary>
        public SensitizationStep(
            string stepId,
            string? requiredPortionCode,
            int requiredAmount,
            string intermediateStageLabel)
            : this(stepId, requiredPortionCode, requiredAmount, intermediateStageLabel, SensitizationStepKind.Chemical, null, null)
        { }

        /// <summary>Full constructor supporting both Chemical and Dry steps.</summary>
        public SensitizationStep(
            string stepId,
            string? requiredPortionCode,
            int requiredAmount,
            string intermediateStageLabel,
            SensitizationStepKind kind,
            float? waitSeconds,
            string? resultPlateNameLangCode = null)
        {
            StepId = stepId;
            RequiredPortionCode = requiredPortionCode;
            RequiredAmount = requiredAmount;
            IntermediateStageLabel = intermediateStageLabel;
            Kind = kind;
            WaitSeconds = waitSeconds;
            ResultPlateNameLangCode = resultPlateNameLangCode;
        }
    }
}

