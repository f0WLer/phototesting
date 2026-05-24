using Phototesting.ImageEffects;

namespace Phototesting.CameraCapture.Exposure
{
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
}
