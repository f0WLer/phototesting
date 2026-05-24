using SkiaSharp;

namespace Phototesting.CameraCapture.Exposure
{
    /// <summary>
    /// Minimal sink contract for routing developed exposure preview frames to a display surface.
    /// Decouples <see cref="VirtualExposureRenderer"/> from any specific preview renderer implementation.
    /// </summary>
    internal interface IExposurePreviewSink
    {
        /// <summary>Called when the exposure renderer begins accumulating, so the preview surface can enter passthrough mode.</summary>
        void BeginExposurePassthrough();
        /// <summary>Called when accumulation ends, reverting the preview surface out of passthrough mode.</summary>
        void EndExposurePassthrough();
        /// <summary>Delivers a developed mid-exposure preview frame to the sink for display.</summary>
        void StoreExposureFrame(SKBitmap bitmap);
    }
}