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
        {
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, fromFbo.FboId);
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, toFbo.FboId);
            GL.BlitFramebuffer(0, 0, fromFbo.Width, fromFbo.Height,
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
