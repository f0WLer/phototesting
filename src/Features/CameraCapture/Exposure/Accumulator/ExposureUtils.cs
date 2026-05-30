using OpenTK.Graphics.OpenGL;
using Vintagestory.API.Client;

namespace Phototesting.CameraCapture.Exposure
{
    /// <summary>Shared utilities for the exposure pipeline: framebuffer operations and color-space math.</summary>
    internal static class ExposureUtils
    {
        /// <summary>
        /// Blits <paramref name="fromFbo"/> into <paramref name="toFbo"/> with a vertical flip,
        /// converting OpenGL's bottom-left-origin image to top-left origin.
        /// </summary>
        internal static void BlitYFlipped(FrameBufferRef fromFbo, FrameBufferRef toFbo)
            => BlitYFlipped(fromFbo.FboId, fromFbo.Width, fromFbo.Height, toFbo);

        /// <summary>
        /// Overload that accepts a raw GL framebuffer ID and dimensions.
        /// Used when the source is not wrapped in a <see cref="FrameBufferRef"/> — e.g. the default back-buffer (ID 0).
        /// </summary>
        internal static void BlitYFlipped(int fromFboId, int fromW, int fromH, FrameBufferRef toFbo)
        {
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, fromFboId);
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, toFbo.FboId);
            GL.BlitFramebuffer(0, 0, fromW, fromH,
                0, toFbo.Height, toFbo.Width, 0,
                ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Linear);
        }

        /// <summary>
        /// Precomputed sRGB-to-linear LUT (256 entries).
        /// Index is the 0–255 sRGB byte value; result is the linearized [0, 1] float.
        /// </summary>
        internal static readonly float[] SRgbToLinear = BuildLinearTable();

        private static float[] BuildLinearTable()
        {
            float[] t = new float[256];
            for (int i = 0; i < 256; i++)
            {
                float c = i / 255f;
                t[i] = c <= 0.04045f
                    ? c / 12.92f
                    : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
            }
            return t;
        }
    }
}
