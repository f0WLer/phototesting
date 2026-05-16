using Phototesting.CameraCapture;
using Phototesting.ImageEffects;

namespace Phototesting.AdminTooling
{
    // Feature-owned command persistence helpers for operator-tooling config writes.
    internal static class OperatorToolingCommandConfigPersistence
    {
        // Persists effects command edits through the shared client save path.
        internal static void PersistEffectsConfig(PhotoTestingModSystem mod, PhotoTestingConfig rootCfg)
        {
            if (mod.ClientApi == null) return;

            rootCfg.Effects ??= new WetplateEffectsConfig();
            rootCfg.Effects.ClampInPlace();
            mod.SaveClientConfig(mod.ClientApi);
        }

        // Persists preview command edits when a branch actually changed state.
        internal static void PersistPreviewConfig(PhotoTestingModSystem mod, PhotoTestingConfig rootCfg, bool changed)
        {
            if (mod.ClientApi == null || !changed) return;

            rootCfg.Viewfinder ??= new ViewfinderConfig();
            rootCfg.Viewfinder.ClampInPlace();
            mod.SaveClientConfig(mod.ClientApi);
        }
    }
}
