using SkiaSharp;
using Vintagestory.API.Client;
using Phototesting.ImageEffects;

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

    /// <summary>
    /// Accumulator variant that ingests frames already read back to CPU memory.
    /// Used with the async PBO readback path in <see cref="ExposureReadbackPipeline"/>.
    /// </summary>
    internal interface ICpuExposureAccumulator : IExposureAccumulator
    {
        /// <summary>
        /// Adds one BGRA8888 frame to the running exposure sum.
        /// Frames whose dimensions differ from <see cref="IExposureAccumulator.Width"/> × <see cref="IExposureAccumulator.Height"/> are silently ignored.
        /// </summary>
        void Accumulate(byte[] bgra, int width, int height);
    }

    /// <summary>
    /// Accumulator variant that ingests frames directly from a GPU framebuffer, with no per-sample CPU readback.
    /// GPU accumulation and downsampling happen in-place; a CPU readback is only performed when
    /// <see cref="IExposureAccumulator.Develop"/> or a full export is requested.
    /// </summary>
    internal interface IGpuExposureAccumulator : IExposureAccumulator
    {
        /// <summary>
        /// Downsamples <paramref name="sourceFbo"/> into the GPU accumulation texture and adds it to the running exposure sum.
        /// <paramref name="sourceFbo"/> must remain valid for the duration of this call.
        /// </summary>
        void Accumulate(FrameBufferRef sourceFbo);
    }

    /// <summary>
    /// Shared interface for the two gameplay-level accumulation-based exposure paths:
    /// the handheld viewport accumulator and the mounted virtual-camera renderer.
    /// Implementations collect rendered frames over time and export a developed PNG when sealed.
    /// </summary>
    internal interface IGameplayExposureAccumulator : IDisposable
    {
        /// <summary>Current lifecycle state of this exposure session.</summary>
        ExposureState State { get; }
        /// <summary><see langword="true"/> while the accumulator is actively collecting frames.</summary>
        bool IsCapturing { get; }
        /// <summary>Number of frames accumulated so far in the current session.</summary>
        int FramesAccumulated { get; }
        /// <summary>Total number of samples required for a fully-exposed plate at the active chemistry.</summary>
        int TargetFrames { get; }

        /// <summary>Suspends frame accumulation without discarding the buffer.</summary>
        void Pause();
        /// <summary>Resumes frame accumulation from a previously paused state.</summary>
        void Resume();

        /// <summary>Finalizes the session without exporting. Unregisters any renderer and transitions to <see cref="ExposureState.Done"/>.</summary>
        void Stop();

        /// <summary>
        /// Develops the buffer, applies wetplate finishing effects, and writes a PNG to the photo store.
        /// Returns the saved file name, suitable for use as <c>PhotoTakenPacket.PhotoId</c>.
        /// Throws when no frames have been accumulated.
        /// </summary>
        string Export(WetplateEffectsConfig? effectsOverride = null);
    }

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
        /// <summary>Resets the idle-preview timer so the next render tick produces a fresh frame.</summary>
        void ForceRefreshNextFrame();
    }
}
