using Vintagestory.API.Common;

namespace Phototesting.CameraCapture
{
    // Per-entity input state helpers for the camera interact loop.
    // Tracks LMB edge detection between frames so single-press semantics work on held interact callbacks.
    public partial class ItemWetplateCamera
    {
        private const string LmbPrevAttrKey = "phototestingCameraLmbPrev";

        // Reads the previous LMB state cached on the entity for edge detection.
        private static bool GetLmbPrev(EntityAgent byEntity)
            => byEntity?.Attributes?.GetBool(LmbPrevAttrKey) ?? false;

        // Stores the current LMB state for the next frame's edge detection.
        private static void SetLmbPrev(EntityAgent byEntity, bool value)
            => byEntity?.Attributes?.SetBool(LmbPrevAttrKey, value);
    }
}

