namespace Phototesting.PlateLifecycle
{
    /// <summary>
    /// Per-process exposure constraints applied when capturing through the viewfinder.
    /// Future fields: sensitivity modifier, reciprocity failure factor.
    /// </summary>
    public sealed class ExposureParameters
    {
        /// <summary>Minimum time the player must hold still for a valid exposure, in seconds.</summary>
        public double MinExposureSeconds { get; }

        /// <summary>Maximum exposure window before the plate is considered over-exposed, in seconds.</summary>
        public double MaxExposureSeconds { get; }

        public ExposureParameters(double minExposureSeconds, double maxExposureSeconds)
        {
            MinExposureSeconds = minExposureSeconds;
            MaxExposureSeconds = maxExposureSeconds;
        }
    }
}

