using SkiaSharp;
using Vintagestory.API.Client;

namespace Phototesting.ImageEffects
{
    // Thin seam for capture/render callers so effects runtime internals can move without touching callsites.
    internal static class ImageEffectsPipelineBridge
    {
        // Loads the active baseline profile from client config storage.
        internal static WetplateEffectsConfig LoadCaptureBaseline(ICoreClientAPI capi)
        {
            return ImageEffectsProfileService.LoadOrCreate(capi);
        }

        // Chooses per-capture override profile when provided, otherwise uses the baseline snapshot.
        internal static WetplateEffectsConfig ResolveCaptureProfile(WetplateEffectsConfig baselineProfile, WetplateEffectsConfig? effectsOverride)
        {
            return effectsOverride != null
                ? ImageEffectsProfileService.CreateRuntimeSnapshot(effectsOverride)
                : baselineProfile;
        }

        // Applies the effects pipeline in-place for one captured bitmap.
        internal static void ApplyCaptureEffects(SKBitmap bitmap, string seedKey, WetplateEffectsConfig profile)
        {
            WetplateEffects.ApplyInPlace(bitmap, seedKey, profile);
        }
    }
}