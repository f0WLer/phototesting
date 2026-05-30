using SkiaSharp;
using Phototesting.ImageEffects;

namespace Phototesting.CameraCapture.Exposure
{
    /// <summary>Lifecycle state of an exposure session on either the viewport or virtual-camera renderer path.</summary>
    internal enum ExposureState { Idle, Capturing, Paused, Faulted, Done }

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

    /// <summary>
    /// Shared state consulted by the Harmony patch on <c>EntityPlayerShapeRenderer</c>
    /// to suppress local-player rendering during viewfinder mode and viewport exposure accumulation.
    /// Only ever read or written on the main game thread.
    /// </summary>
    internal static class ViewportExposureSuppressContext
    {
        /// <summary>True while the player is in viewfinder mode (RMB held or exposure keeping it alive).</summary>
        internal static bool ViewfinderActive;
        /// <summary>True while the viewport exposure accumulator is actively gathering frames.</summary>
        internal static bool ExposureCapturing;
        /// <summary>When <see langword="true"/>, the patched renderer skips drawing the local player for the current frame.</summary>
        internal static bool SuppressLocalPlayer => ViewfinderActive || ExposureCapturing;
    }
}
