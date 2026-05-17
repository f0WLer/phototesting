using Vintagestory.API.MathTools;

namespace Phototesting.CameraCapture
{
    // Thread-local state shared with EntityPlayerSelfPortraitPatch for one virtual render pass.
    internal static class VirtualCameraSelfPortraitContext
    {
        [System.ThreadStatic]
        internal static bool Active;

        [System.ThreadStatic]
        private static float[]? _tmpTranslate;

        [System.ThreadStatic]
        private static float[]? _tmpModel;

        internal static float[] TmpTranslate => _tmpTranslate ??= Vintagestory.API.MathTools.Mat4f.Create();
        internal static float[] TmpModel    => _tmpModel    ??= Vintagestory.API.MathTools.Mat4f.Create();
    }
}
