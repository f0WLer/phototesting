using SkiaSharp;

namespace Phototesting.CameraCapture.Exposure
{
    // Minimal sink contract for feeding developed exposure frames into a preview surface.
    // Keeps VirtualExposureRenderer independent from any specific preview renderer implementation.
    internal interface IExposurePreviewSink
    {
        void BeginExposurePassthrough();
        void EndExposurePassthrough();
        void StoreExposureFrame(SKBitmap bitmap);
    }
}