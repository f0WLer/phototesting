using Phototesting.PlateLifecycle.Tray.Config;
using Phototesting.ImageEffects;

namespace Phototesting.AdminTooling
{
    // Root persisted config tree for phototesting systems.
    // Aggregates subsystem configs and enforces safe ranges through ClampInPlace.
    public sealed class PhotoTestingConfig
    {
        public PhotoTestingClientConfig Client = new();
        public WetplateEffectsConfig Effects = new();
        public PhotographConfig Photograph = new();
        public PlateProcessingConfig PlateProcessing = new();
        public PhotoSyncConfig PhotoSync = new();
        public PhotoCapturePipelineConfig PhotoCapturePipeline = new();

        // Viewfinder capture behavior (capture runs client-side; server provides authoritative limits in multiplayer).
        public ViewfinderConfig Viewfinder = new();

        // Timed interaction configuration (shared by client/server).
        public DevelopmentTrayInteractionConfig DevelopmentTrayInteractions = new();

        // Optional presets (editable via .phototesting effects preset ...)
        public WetplateEffectsConfig EffectsPresetIndoor = new();
        public WetplateEffectsConfig EffectsPresetOutdoor = new();

        // Clamps and initializes nested config branches so runtime access stays null-safe and bounded.
        internal void ClampInPlace()
        {
            Client ??= new PhotoTestingClientConfig();
            Client.ClampInPlace();

            Effects ??= new WetplateEffectsConfig();
            Effects.ClampInPlace();

            Photograph ??= new PhotographConfig();
            Photograph.ClampInPlace();

            PlateProcessing ??= new PlateProcessingConfig();
            PlateProcessing.ClampInPlace();

            PhotoSync ??= new PhotoSyncConfig();
            PhotoSync.ClampInPlace();

            PhotoCapturePipeline ??= new PhotoCapturePipelineConfig();
            PhotoCapturePipeline.ClampInPlace();

            EffectsPresetIndoor ??= new WetplateEffectsConfig();
            EffectsPresetIndoor.ClampInPlace();

            EffectsPresetOutdoor ??= new WetplateEffectsConfig();
            EffectsPresetOutdoor.ClampInPlace();

            DevelopmentTrayInteractions ??= new DevelopmentTrayInteractionConfig();
            DevelopmentTrayInteractions.ClampInPlace();

            Viewfinder ??= new ViewfinderConfig();
            Viewfinder.ClampInPlace();
        }
    }

}
