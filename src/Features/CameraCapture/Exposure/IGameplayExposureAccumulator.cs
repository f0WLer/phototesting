using Phototesting.ImageEffects;

namespace Phototesting.CameraCapture.Exposure
{
    // Shared interface for accumulation-based exposure implementations (viewport handheld, virtual tripod).
    // Implementations accumulate rendered frames and export a developed image when sealed.
    internal interface IGameplayExposureAccumulator : IDisposable
    {
        ExposureState State { get; }
        bool IsCapturing { get; }
        int FramesAccumulated { get; }
        int TargetFrames { get; }

        void Pause();
        void Resume();

        // Finalizes the session without exporting. Unregisters any renderer and transitions to Done.
        void Stop();

        // Develops the buffer, applies wetplate finishing, and writes a PNG to the photo store.
        // Returns the file name (suitable for PhotoTakenPacket.PhotoId).
        // Throws when no frames have been accumulated.
        string Export(WetplateEffectsConfig? effectsOverride = null);
    }
}
