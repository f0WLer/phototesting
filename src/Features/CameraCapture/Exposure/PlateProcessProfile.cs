namespace Phototesting.CameraCapture.Exposure
{
    /// <summary>
    /// Immutable emulsion parameters for a single wet-plate chemistry variant.
    /// Defines shutter timing (<see cref="DurationSeconds"/>, <see cref="SampleCount"/>) and the
    /// spectral sensitivity and H&amp;D curve values that shape how the accumulation buffer develops.
    /// </summary>
    internal readonly struct PlateProcessProfile
    {
        internal readonly string Name;
        internal readonly float DurationSeconds;
        internal readonly int   SampleCount;
        internal readonly float RedSensitivity;
        internal readonly float GreenSensitivity;
        internal readonly float BlueSensitivity;
        internal readonly float DevelopmentStrength;
        internal readonly float HDGamma;

        /// <summary>Wall-clock seconds between consecutive virtual renders at normal cadence.</summary>
        internal float SampleInterval => DurationSeconds / SampleCount;

        /// <summary>
        /// ISO-equivalent film speed: reciprocal of <see cref="DurationSeconds"/>.
        /// Faster (higher ISO) processes have shorter durations; all produce the same output density at full exposure
        /// because <see cref="DevelopmentStrength"/> is consistent across presets.
        /// </summary>
        internal float IsoEquivalent => 1f / DurationSeconds;

        internal PlateProcessProfile(
            string name, float durationSeconds, int sampleCount,
            float redSensitivity, float greenSensitivity, float blueSensitivity,
            float developmentStrength, float hdGamma)
        {
            Name = name;
            DurationSeconds = durationSeconds;
            SampleCount = sampleCount;
            RedSensitivity = redSensitivity;
            GreenSensitivity = greenSensitivity;
            BlueSensitivity = blueSensitivity;
            DevelopmentStrength = developmentStrength;
            HDGamma = hdGamma;
        }

        /// <summary>Early wet-plate collodion: strongly blue-shifted, very slow (~90 s for correct exposure). Tripod almost always required.</summary>
        internal static readonly PlateProcessProfile Chloride = new PlateProcessProfile(
            "Chloride", durationSeconds: 90f, sampleCount: 128,
            redSensitivity: 0.04f, greenSensitivity: 0.35f, blueSensitivity: 1.00f,
            developmentStrength: 8.0f, hdGamma: 1.15f);

        /// <summary>Mid-tier silver iodide: expanded spectral response, moderate speed (~20 s for correct exposure). Tripod recommended for moving subjects.</summary>
        internal static readonly PlateProcessProfile Iodide = new PlateProcessProfile(
            "Iodide", durationSeconds: 20f, sampleCount: 64,
            redSensitivity: 0.12f, greenSensitivity: 0.45f, blueSensitivity: 1.00f,
            developmentStrength: 8.0f, hdGamma: 1.10f);

        /// <summary>Advanced silver-bromide gelatin: panchromatic, fast (~3 s for correct exposure). Handheld shots viable; tripod still improves results.</summary>
        internal static readonly PlateProcessProfile Bromide = new PlateProcessProfile(
            "Bromide", durationSeconds: 3f, sampleCount: 32,
            redSensitivity: 0.30f, greenSensitivity: 0.59f, blueSensitivity: 1.00f,
            developmentStrength: 8.0f, hdGamma: 1.05f);

        /// <summary>Parses a chemistry name (case-insensitive) into a <see cref="PlateProcessProfile"/>. Returns <see langword="false"/> when the name is unrecognised.</summary>
        internal static bool TryParse(string name, out PlateProcessProfile profile)
        {
            switch (name.ToLowerInvariant())
            {
                case "chloride": profile = Chloride; return true;
                case "iodide":   profile = Iodide;   return true;
                case "bromide":  profile = Bromide;  return true;
                default:         profile = default;  return false;
            }
        }
    }
}
