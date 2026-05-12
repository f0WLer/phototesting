namespace Phototesting.PlateLifecycle
{
    /// <summary>
    /// Describes the full characteristics and pipeline of a photographic plate chemistry process,
    /// covering sensitization steps, exposure constraints, and development behavior.
    /// </summary>
    public sealed class PhotographyProcessDefinition
    {
        /// <summary>Unique machine identifier; matches the <see cref="PlateStateAttributes.ProcessId"/> attribute value.</summary>
        public string Id { get; }

        /// <summary>Human-readable name shown in tooltips and commands.</summary>
        public string DisplayName { get; }

        /// <summary>
        /// Effects profile name used when capturing/rendering images for this process.
        /// Prefers ModData/phototesting/{name}.json and falls back to bundled mod asset
        /// phototesting:config/effects/{name}.json when no user override exists.
        /// </summary>
        public string DefaultEffectsProfile { get; }

        /// <summary>
        /// Ordered steps the player must perform to take a clean plate to sensitized.
        /// All steps complete in sequence before the plate becomes <see cref="PlateStage.Sensitized"/>
        /// and the process ID is stamped.
        /// </summary>
        public IReadOnlyList<SensitizationStep> SensitizationSteps { get; }

        /// <summary>Exposure constraints and parameters for this process.</summary>
        public ExposureParameters Exposure { get; }

        /// <summary>
        /// Multiplier applied to base wet duration for this process.
        /// 0.0 means the plate never dries; 1.0 uses the configured duration unchanged.
        /// </summary>
        public double WetDurationMultiplier { get; }

        /// <summary>
        /// Ordered development/fixing/rinse pipeline for this process.
        /// </summary>
        public IReadOnlyList<DevelopmentStep> DevelopmentPipeline { get; }

        /// <summary>Default display name language key when a plate reaches the sensitized stage.</summary>
        public string SensitizedPlateNameLangCode { get; }

        /// <summary>Default display name language key when a plate reaches the exposed stage.</summary>
        public string ExposedPlateNameLangCode { get; }

        /// <summary>Default display name language key when a plate is developed (image visible).</summary>
        public string DevelopedPlateNameLangCode { get; }

        /// <summary>Default display name language key when a plate is fixed/finished.</summary>
        public string FinishedPlateNameLangCode { get; }

        public PhotographyProcessDefinition(
            string id,
            string displayName,
            string defaultEffectsProfile,
            IReadOnlyList<SensitizationStep> sensitizationSteps,
            ExposureParameters exposure,
            double wetDurationMultiplier,
            IReadOnlyList<DevelopmentStep> developmentPipeline,
            string sensitizedPlateNameLangCode = "phototesting:plate-name-sensitized",
            string exposedPlateNameLangCode = "phototesting:plate-name-exposed",
            string developedPlateNameLangCode = "phototesting:plate-name-photo",
            string finishedPlateNameLangCode = "phototesting:plate-name-photo-finished")
        {
            Id = id;
            DisplayName = displayName;
            DefaultEffectsProfile = defaultEffectsProfile;
            SensitizationSteps = sensitizationSteps;
            Exposure = exposure;
            WetDurationMultiplier = wetDurationMultiplier;
            DevelopmentPipeline = developmentPipeline;
            SensitizedPlateNameLangCode = sensitizedPlateNameLangCode;
            ExposedPlateNameLangCode = exposedPlateNameLangCode;
            DevelopedPlateNameLangCode = developedPlateNameLangCode;
            FinishedPlateNameLangCode = finishedPlateNameLangCode;
        }
    }
}

