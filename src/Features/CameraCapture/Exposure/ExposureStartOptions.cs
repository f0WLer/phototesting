namespace Phototesting.CameraCapture.Exposure
{
    internal enum ExposureStopMode
    {
        Manual = 0,
        Timer = 1,
        TargetSamples = 2
    }

    internal readonly record struct ExposureStartOptions(ExposureStopMode StopMode, float StopAfterSeconds = 0f)
    {
        internal static ExposureStartOptions Manual() => new(ExposureStopMode.Manual);

        internal static ExposureStartOptions Timer(float stopAfterSeconds)
            => new(ExposureStopMode.Timer, Math.Max(0f, stopAfterSeconds));

        internal static ExposureStartOptions TargetSamples() => new(ExposureStopMode.TargetSamples);
    }
}