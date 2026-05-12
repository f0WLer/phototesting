using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Phototesting.CameraCapture
{
    // Timed exposure helpers and transient per-entity exposure state.
    // Contains duration clamping, elapsed-time reads, and left-click state caching.
    public partial class ItemWetplateCamera
    {
        private static readonly AssetLocation _exposureStartSound = new("phototesting", "sounds/exposure-start");
        private static readonly AssetLocation _exposureFinishSound = new("phototesting", "sounds/exposure-end");

        // Returns world elapsed ms, falling back to TickCount64 if the world clock is unavailable.
        private static long GetWorldMs(EntityAgent byEntity)
        {
            try { long ms = byEntity.World?.ElapsedMilliseconds ?? 0; if (ms > 0) return ms; }
            catch { /* intentional: world may be unavailable */ }
            return Environment.TickCount64;
        }

        // Starts the timed exposure state.
        private static void BeginTimedExposure(EntityAgent byEntity, float durationSeconds)
        {
            if (byEntity?.Attributes == null) return;

            ITreeAttribute tree = byEntity.Attributes.GetOrAddTreeAttribute(ExposureTimedAttrKey);

            long nowMs = GetWorldMs(byEntity);
            tree.SetLong(ExposureTimedStartMsKey, nowMs);

            int durationMs = (int)Math.Round(durationSeconds * 1000f);
            if (durationMs < 1) durationMs = 1;
            tree.SetInt(ExposureTimedDurationMsKey, durationMs);
        }

        // Clears the temporary exposure timing tree once the shot has completed or been abandoned.
        private static void ClearTimedExposure(EntityAgent byEntity)
        {
            byEntity?.Attributes?.RemoveAttribute(ExposureTimedAttrKey);
        }

        // Checks whether timed exposure is active.
        private static bool IsTimedExposureActive(EntityAgent byEntity, out float durationSeconds)
        {
            durationSeconds = 0f;
            if (byEntity?.Attributes == null) return false;

            ITreeAttribute? tree = byEntity.Attributes.GetTreeAttribute(ExposureTimedAttrKey);
            if (tree == null) return false;

            int durationMs = tree.GetInt(ExposureTimedDurationMsKey, 0);
            if (durationMs <= 0) return false;

            durationSeconds = durationMs / 1000f;
            return durationSeconds > 0f;
        }

        // Gets elapsed timed-exposure seconds.
        private static float GetTimedExposureElapsedSeconds(EntityAgent byEntity)
        {
            if (byEntity?.Attributes == null) return 0f;

            ITreeAttribute? tree = byEntity.Attributes.GetTreeAttribute(ExposureTimedAttrKey);
            if (tree == null) return 0f;

            long startMs = tree.GetLong(ExposureTimedStartMsKey, 0);
            if (startMs <= 0) return 0f;

            long nowMs = GetWorldMs(byEntity);
            if (nowMs <= startMs) return 0f;

            return (nowMs - startMs) / 1000f;
        }

        // Reads cached previous LMB state.
        private static bool GetLmbPrev(EntityAgent byEntity)
        {
            if (byEntity?.Attributes == null) return false;
            ITreeAttribute? tree = byEntity.Attributes.GetTreeAttribute(ExposureTimedAttrKey);
            return tree?.GetBool(ExposureLmbPrevKey, false) ?? false;
        }

        // Stores previous LMB state.
        private static void SetLmbPrev(EntityAgent byEntity, bool value)
        {
            if (byEntity?.Attributes == null) return;

            ITreeAttribute tree = byEntity.Attributes.GetOrAddTreeAttribute(ExposureTimedAttrKey);
            tree.SetBool(ExposureLmbPrevKey, value);
        }
    }
}
