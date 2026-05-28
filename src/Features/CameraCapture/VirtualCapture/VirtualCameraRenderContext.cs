namespace Phototesting.CameraCapture
{
    // Static context flag set by VirtualCamera.RenderCamera for the duration of one virtual render pass.
    internal static class VirtualCameraRenderContext
    {
        /// <summary>
        /// True while a virtual camera render is in progress.
        /// Consulted by renderers that must not contribute geometry to virtual captures
        /// (e.g., the mounted camera block entity renderer).
        /// </summary>
        internal static bool IsVirtualRender;
    }
}
