using SkiaSharp;

namespace Phototesting.CameraCapture.Exposure
{
    /// <summary>
    /// Shared interface for CPU and GPU exposure accumulation backends.
    /// Covers physics settings, state queries, buffer reset, and final development to a bitmap.
    /// Sample ingestion is not part of this interface because the CPU and GPU paths take
    /// different input types; see <see cref="ICpuExposureAccumulator"/> and
    /// <see cref="IGpuExposureAccumulator"/> for the respective ingest contracts.
    /// </summary>
    internal interface IExposureAccumulator : IDisposable
    {
        /// <summary>Width of the accumulation buffer in pixels.</summary>
        int Width  { get; }
        /// <summary>Height of the accumulation buffer in pixels.</summary>
        int Height { get; }
        /// <summary>Number of frames added to the buffer since the last <see cref="Reset"/>.</summary>
        int FramesAccumulated { get; }

        // Physics toggles — must be set before samples arrive or before Develop() is called.
        /// <summary>Convert sRGB input pixels to linear light before accumulating. Disable to accumulate in gamma space (faster but physically incorrect).</summary>
        bool LinearizeInput          { get; set; }
        /// <summary>Collapse RGB channels to a single silver-density value using historical spectral weights, producing a greyscale developed image.</summary>
        bool ApplySpectralWeights    { get; set; }
        /// <summary>Apply the Hurter-Driffield characteristic curve during development; highlights compress naturally instead of hard-clipping.</summary>
        bool ApplyHDCurve            { get; set; }
        /// <summary>Normalise developed brightness by the actual accumulated frame count rather than the reference count. Use when wall-clock duration controls shutter close.</summary>
        bool NormalizeByActualFrameCount { get; set; }

        // Spectral sensitivity weights (normalised internally).
        /// <summary>Red-channel emulsion sensitivity weight. Normalised against the sum of all three channels internally.</summary>
        float RedSensitivity   { get; set; }
        /// <summary>Green-channel emulsion sensitivity weight. Normalised against the sum of all three channels internally.</summary>
        float GreenSensitivity { get; set; }
        /// <summary>Blue-channel emulsion sensitivity weight. Normalised against the sum of all three channels internally.</summary>
        float BlueSensitivity  { get; set; }

        // H&D curve parameters.
        /// <summary>Controls how quickly the H&amp;D curve rises; higher values produce a brighter overall image.</summary>
        float DevelopmentStrength { get; set; }
        /// <summary>Contrast of the developed image; higher values produce more contrasty output.</summary>
        float HDGamma             { get; set; }

        /// <summary>Clears all accumulated data so the next sample starts from a clean slate.</summary>
        void Reset();

        /// <summary>
        /// Develops the current accumulated state into a new BGRA8888 bitmap.
        /// Returns a black image when no frames have been accumulated.
        /// The caller owns and is responsible for disposing the returned bitmap.
        /// </summary>
        SKBitmap Develop();

        /// <summary>
        /// Serializes the raw float accumulation sums and frame count into a self-describing binary blob
        /// for cross-session persistence via <see cref="ExposureAccumulationStore"/>.
        /// Returns <see langword="null"/> when no frames have been accumulated.
        /// </summary>
        byte[]? SerializeAccumulation();

        /// <summary>
        /// Restores float accumulation sums from a blob previously produced by <see cref="SerializeAccumulation"/>.
        /// Returns <see langword="false"/> when the blob is incompatible (wrong magic bytes, dimension mismatch, or corrupt).
        /// </summary>
        bool DeserializeAccumulation(byte[] data, out int frameCount);
    }
}
