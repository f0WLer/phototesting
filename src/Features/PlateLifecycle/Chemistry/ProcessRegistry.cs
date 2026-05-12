namespace Phototesting.PlateLifecycle
{
    /// <summary>
    /// Holds all registered <see cref="PhotographyProcessDefinition"/>s and resolves a process
    /// by ID, falling back to the built-in iodide default for legacy plates with no process attribute.
    /// </summary>
    public sealed class ProcessRegistry
    {
        // ---------------------------------------------------------------------------
        // Shared sub-parameters (reused across multiple built-in processes)
        // ---------------------------------------------------------------------------

        private static readonly ExposureParameters _defaultExposure = new ExposureParameters(
            minExposureSeconds: 0.5,
            maxExposureSeconds: 8.0);

        private const double StandardWetDurationMultiplier = 1.0;

        private static readonly IReadOnlyList<DevelopmentStep> _standardPipeline = new DevelopmentStep[]
            {
                new DevelopmentStep(
                    stepId: "develop",
                    actionKind: DevelopmentActionKind.Developer,
                    requiredPortionCode: "phototesting:developerportion",
                    requiredAmount: 40,
                    requiredApplications: 5,
                    waitSeconds: null,
                    intermediateStage: PlateStage.Developing,
                    resultStage: PlateStage.Developed,
                    resultPlateNameLangCode: null),
                new DevelopmentStep(
                    stepId: "fix",
                    actionKind: DevelopmentActionKind.Fixer,
                    requiredPortionCode: "phototesting:fixerportion",
                    requiredAmount: 40,
                    requiredApplications: 1,
                    waitSeconds: null,
                    intermediateStage: PlateStage.Developed,
                    resultStage: PlateStage.Finished,
                    resultPlateNameLangCode: null),
                new DevelopmentStep(
                    stepId: "rinse",
                    actionKind: DevelopmentActionKind.Water,
                    requiredPortionCode: "game:waterportion",
                    requiredAmount: 40,
                    requiredApplications: 1,
                    waitSeconds: null,
                    intermediateStage: PlateStage.Finished,
                    resultStage: PlateStage.Rough,
                    resultPlateNameLangCode: "phototesting:plate-name-glass")
            };

        private const double BromideWetDurationMultiplier = 0.0;

        // ---------------------------------------------------------------------------
        // Built-in process definitions
        // ---------------------------------------------------------------------------

        /// <summary>
        /// The iodide wet-plate process — default for all legacy plates with no process attribute.
        /// Requires collodion coating then a silver bath before exposure.
        /// </summary>
        public static readonly PhotographyProcessDefinition DefaultProcess =
            new PhotographyProcessDefinition(
                id: PlateStateAttributes.DefaultProcessId,
                displayName: "Iodide Wet Plate",
                defaultEffectsProfile: "iodide",
                sensitizationSteps: new SensitizationStep[]
                {
                    new SensitizationStep("coat-collodion", "phototesting:collodionportion",      40, "sensitizing", SensitizationStepKind.Chemical, null, "phototesting:plate-name-iodide-coated"),
                    new SensitizationStep("silver-bath",    "phototesting:silversolutionportion",  40, "sensitizing", SensitizationStepKind.Chemical, null, "phototesting:plate-name-sensitized"),
                },
                exposure: _defaultExposure,
                wetDurationMultiplier: StandardWetDurationMultiplier,
                developmentPipeline: _standardPipeline,
                sensitizedPlateNameLangCode: "phototesting:plate-name-sensitized",
                exposedPlateNameLangCode: "phototesting:plate-name-exposed",
                developedPlateNameLangCode: "phototesting:plate-name-photo",
                finishedPlateNameLangCode: "phototesting:plate-name-photo-finished");

        /// <summary>
        /// Silver chloride process — chloride-solution coating, air dry, then silver bath.
        /// </summary>
        public static readonly PhotographyProcessDefinition ChlorideProcess =
            new PhotographyProcessDefinition(
                id: "chloride",
                displayName: "Silver Chloride Plate",
                defaultEffectsProfile: "chloride",
                sensitizationSteps: new SensitizationStep[]
                {
                    new SensitizationStep("coat-chloride",  "phototesting:chloridesolutionportion", 40, "sensitizing", SensitizationStepKind.Chemical, null, "phototesting:plate-name-chloride-coated"),
                    new SensitizationStep("air-dry",        null, 0, "sensitizing", SensitizationStepKind.Dry, 30f, "phototesting:plate-name-dried-coated"),
                    new SensitizationStep("silver-bath",    "phototesting:silversolutionportion",  40, "sensitizing", SensitizationStepKind.Chemical, null, "phototesting:plate-name-sensitized"),
                },
                exposure: _defaultExposure,
                wetDurationMultiplier: StandardWetDurationMultiplier,
                developmentPipeline: _standardPipeline,
                sensitizedPlateNameLangCode: "phototesting:plate-name-sensitized",
                exposedPlateNameLangCode: "phototesting:plate-name-exposed",
                developedPlateNameLangCode: "phototesting:plate-name-photo",
                finishedPlateNameLangCode: "phototesting:plate-name-photo-finished");

        /// <summary>
        /// Gelatin silver bromide process — bromide emulsion coating then air-dry; plate never dries after sensitization.
        /// </summary>
        public static readonly PhotographyProcessDefinition BromideProcess =
            new PhotographyProcessDefinition(
                id: "bromide",
                displayName: "Silver Bromide Plate",
                defaultEffectsProfile: "bromide",
                sensitizationSteps: new SensitizationStep[]
                {
                    new SensitizationStep("coat-emulsion",  "phototesting:bromideemulsion",        40, "sensitizing", SensitizationStepKind.Chemical, null, "phototesting:plate-name-bromide-coated"),
                    new SensitizationStep("air-dry",        null, 0, "sensitizing", SensitizationStepKind.Dry, 45f, "phototesting:plate-name-dried-coated"),
                },
                exposure: _defaultExposure,
                wetDurationMultiplier: BromideWetDurationMultiplier,
                developmentPipeline: _standardPipeline,
                sensitizedPlateNameLangCode: "phototesting:plate-name-sensitized",
                exposedPlateNameLangCode: "phototesting:plate-name-exposed",
                developedPlateNameLangCode: "phototesting:plate-name-photo",
                finishedPlateNameLangCode: "phototesting:plate-name-photo-finished");

        // ---------------------------------------------------------------------------
        // Registry instance
        // ---------------------------------------------------------------------------

        private readonly Dictionary<string, PhotographyProcessDefinition> _processes =
            new Dictionary<string, PhotographyProcessDefinition>(StringComparer.OrdinalIgnoreCase);

        public ProcessRegistry()
        {
            _processes[DefaultProcess.Id] = DefaultProcess;
            _processes[ChlorideProcess.Id] = ChlorideProcess;
            _processes[BromideProcess.Id] = BromideProcess;
        }

        /// <summary>
        /// Returns the definition for <paramref name="processId"/>, or <see cref="DefaultProcess"/>
        /// if the ID is null, empty, or unrecognized.
        /// </summary>
        public PhotographyProcessDefinition ResolveOrDefault(string? processId)
        {
            if (!string.IsNullOrEmpty(processId) && _processes.TryGetValue(processId, out var def))
                return def;

            return DefaultProcess;
        }

        /// <summary>All currently registered process definitions, keyed by ID.</summary>
        public IReadOnlyDictionary<string, PhotographyProcessDefinition> AllProcesses => _processes;
    }
}

