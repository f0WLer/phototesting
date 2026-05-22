using OpenTK.Graphics.OpenGL;
using Vintagestory.API.Client;

namespace Phototesting.CameraCapture.Exposure
{
    // Shared GL helpers used by both the CPU readback pipeline and the GPU accumulator.
    internal static class ExposureGlUtils
    {
        // Blits fromFbo → toFbo with a Y-flip, converting GL's bottom-left origin to top-left.
        // Sets read/draw framebuffer bindings; caller must save/restore them if needed.
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
