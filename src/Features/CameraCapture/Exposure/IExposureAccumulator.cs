using SkiaSharp;

namespace Phototesting.CameraCapture.Exposure
{
    // Shared interface for CPU and GPU exposure accumulation backends.
    // Covers settings, state queries, reset, and final development to a bitmap.
    // Sample ingestion is intentionally not part of this interface because the
    // CPU and GPU paths require different input types; see ICpuExposureAccumulator
    // and IGpuExposureAccumulator for the respective ingest contracts.
    internal interface IExposureAccumulator : IDisposable
    {
        int Width  { get; }
        int Height { get; }
        int FramesAccumulated { get; }

        // Physics toggles — must be set before samples arrive or before Develop() is called.
        bool LinearizeInput          { get; set; }
        bool ApplySpectralWeights    { get; set; }
        bool ApplyHDCurve            { get; set; }
        bool NormalizeByActualFrameCount { get; set; }

        // Spectral sensitivity weights (normalised internally).
        float RedSensitivity   { get; set; }
        float GreenSensitivity { get; set; }
        float BlueSensitivity  { get; set; }

        // H&D curve parameters.
        float DevelopmentStrength { get; set; }
        float HDGamma             { get; set; }

        // Clears all accumulated data so the next sample starts from a clean state.
        void Reset();

        // Develops the current accumulated state into a new BGRA8888 bitmap.
        // Returns a black image when no frames have been accumulated.
        // Caller owns and is responsible for disposing the returned bitmap.
        SKBitmap Develop();

        // Serializes the raw float accumulation sums and frame count into a compact binary blob
        // for cross-session persistence. Returns null when no frames have been accumulated.
        // The blob contains its own dimension/channel header and is self-describing.
        byte[]? SerializeAccumulation();

        // Restores float accumulation sums from a blob previously produced by SerializeAccumulation.
        // Returns false when the blob is incompatible (wrong magic, dimensions mismatch, or corrupt).
        bool DeserializeAccumulation(byte[] data, out int frameCount);
    }
}
