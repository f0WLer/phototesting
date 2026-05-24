namespace Phototesting.CameraCapture.Exposure
{
    /// <summary>Determines what condition closes the shutter on an in-progress exposure.</summary>
    internal enum ExposureStopMode
    {
        /// <summary>Shutter stays open until the player manually pauses or stops the exposure.</summary>
        Manual = 0,
        /// <summary>Shutter closes automatically after <see cref="ExposureStartOptions.StopAfterSeconds"/> wall-clock seconds.</summary>
        Timer = 1,
        /// <summary>Shutter closes automatically once the plate chemistry's target sample count is reached.</summary>
        TargetSamples = 2
    }

    /// <summary>
    /// Immutable options that determine how and when an exposure session terminates.
    /// Passed to <c>Start()</c> on both the viewport accumulator and virtual-camera renderer paths.
    /// </summary>
    internal readonly record struct ExposureStartOptions(ExposureStopMode StopMode, float StopAfterSeconds = 0f)
    {
        /// <summary>Open-ended exposure; the player controls when the shutter closes.</summary>
        internal static ExposureStartOptions Manual() => new(ExposureStopMode.Manual);

        /// <summary>Exposure closes automatically after the given number of wall-clock seconds.</summary>
        internal static ExposureStartOptions Timer(float stopAfterSeconds)
            => new(ExposureStopMode.Timer, Math.Max(0f, stopAfterSeconds));

        /// <summary>Exposure closes automatically once the plate chemistry's full target sample count is accumulated.</summary>
        internal static ExposureStartOptions TargetSamples() => new(ExposureStopMode.TargetSamples);

        /// <summary>Reconstructs typed options from the integer stop mode stored in a network packet.</summary>
        internal static ExposureStartOptions FromStopModeInt(int stopMode, float stopAfterSeconds = 0f) =>
            new(stopMode switch { 1 => ExposureStopMode.Timer, 2 => ExposureStopMode.TargetSamples, _ => ExposureStopMode.Manual },
                Math.Max(0f, stopAfterSeconds));
    }
}