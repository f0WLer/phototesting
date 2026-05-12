namespace Phototesting.PlateLifecycle
{
    /// <summary>
    /// Action category for one development step.
    /// </summary>
    public enum DevelopmentActionKind
    {
        Developer,
        Fixer,
        Water
    }

    /// <summary>
    /// Immutable definition of one process-specific development step.
    /// </summary>
    public sealed class DevelopmentStep
    {
        /// <summary>Stable step identifier used for process data and debugging.</summary>
        public string StepId { get; }
        /// <summary>Which action type this step performs (developer/fixer/water/dry).</summary>
        public DevelopmentActionKind ActionKind { get; }
        /// <summary>Required portion code for chemical steps; null for dry waits.</summary>
        public string? RequiredPortionCode { get; }
        /// <summary>Chemical amount consumed per application.</summary>
        public int RequiredAmount { get; }
        /// <summary>How many applications are required to complete the step.</summary>
        public int RequiredApplications { get; }
        /// <summary>Optional duration override (seconds) for waiting steps.</summary>
        public float? WaitSeconds { get; }
        /// <summary>Stage applied while the step is in progress.</summary>
        public PlateStage IntermediateStage { get; }
        /// <summary>Stage applied after the step is complete.</summary>
        public PlateStage ResultStage { get; }
        /// <summary>Optional name language-key override applied on completion.</summary>
        public string? ResultPlateNameLangCode { get; }

        public DevelopmentStep(
            string stepId,
            DevelopmentActionKind actionKind,
            string? requiredPortionCode,
            int requiredAmount,
            int requiredApplications,
            float? waitSeconds,
            PlateStage intermediateStage,
            PlateStage resultStage,
            string? resultPlateNameLangCode = null)
        {
            StepId = stepId;
            ActionKind = actionKind;
            RequiredPortionCode = requiredPortionCode;
            RequiredAmount = requiredAmount < 0 ? 0 : requiredAmount;
            RequiredApplications = requiredApplications < 1 ? 1 : requiredApplications;
            WaitSeconds = waitSeconds;
            IntermediateStage = intermediateStage;
            ResultStage = resultStage;
            ResultPlateNameLangCode = resultPlateNameLangCode;
        }
    }
}

