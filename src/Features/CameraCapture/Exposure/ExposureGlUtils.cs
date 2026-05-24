using OpenTK.Graphics.OpenGL;
using Vintagestory.API.Client;

namespace Phototesting.CameraCapture.Exposure
{
    /// <summary>Shared OpenGL helpers used by both <see cref="ExposureReadbackPipeline"/> and <see cref="GpuExposureAccumulator"/>.</summary>
    internal static class ExposureGlUtils
    {
        /// <summary>
        /// Blits <paramref name="fromFbo"/> into <paramref name="toFbo"/> with a vertical flip,
        /// converting OpenGL's bottom-left origin to a top-left coordinate system.
        /// Modifies the current read/draw framebuffer bindings; the caller must save and restore them if needed.
        /// </summary>
        internal static void BlitYFlipped(FrameBufferRef fromFbo, FrameBufferRef toFbo)
        {
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, fromFbo.FboId);
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, toFbo.FboId);
            GL.BlitFramebuffer(
                0, 0, fromFbo.Width, fromFbo.Height,
                0, toFbo.Height, toFbo.Width, 0,    // dst Y inverted = vertical flip
                ClearBufferMask.ColorBufferBit,
                BlitFramebufferFilter.Linear);
        }
    }
}
