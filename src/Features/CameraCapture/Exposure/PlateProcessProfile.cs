namespace Phototesting.CameraCapture.Exposure
{
    internal enum PlateProcess { Chloride, Iodide, Bromide }

    // Per-process emulsion parameters. Defines both the exposure timing (duration + sample count)
    // and the spectral/H&D response that shapes the developed image.
    //
    // DurationSeconds: target wall-clock shutter duration for a correct exposure.
    // SampleCount: virtual renders accumulated to reach normal exposure (referenceFrameCount in the buffer).
    // SampleInterval: wall-clock seconds between renders; derived as DurationSeconds / SampleCount.
    //
    // Spectral weights reflect historical emulsion sensitivity (orthochromatic → panchromatic).
    // H&D curve parameters tune contrast and highlight roll-off independently per process.
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

        // Wall-clock seconds between consecutive virtual renders.
        internal float SampleInterval => DurationSeconds / SampleCount;

        // Reciprocal of DurationSeconds — a process that needs X seconds for a correct exposure
        // in standard noon daylight has ISO = 1/X.  Faster (higher ISO) processes have shorter
        // DurationSeconds; all processes produce the same output density at full exposure because
        // DevelopmentStrength is kept consistent across presets.
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

        // Early wet-plate collodion: strongly blue-shifted, very slow.
        // Almost always requires a tripod. ~90 s for normal exposure.
        internal static readonly PlateProcessProfile Chloride = new PlateProcessProfile(
            "Chloride", durationSeconds: 90f, sampleCount: 128,
            redSensitivity: 0.04f, greenSensitivity: 0.35f, blueSensitivity: 1.00f,
            developmentStrength: 8.0f, hdGamma: 1.15f);

        // Mid-tier: expanded spectral response, moderate speed. ~20 s for normal exposure.
        // Tripod recommended for moving subjects.
        internal static readonly PlateProcessProfile Iodide = new PlateProcessProfile(
            "Iodide", durationSeconds: 20f, sampleCount: 64,
            redSensitivity: 0.12f, greenSensitivity: 0.45f, blueSensitivity: 1.00f,
            developmentStrength: 8.0f, hdGamma: 1.10f);

        // Advanced silver-bromide gelatin: panchromatic, fast. ~3 s for normal exposure.
        // Handheld shots viable; tripod for best results but not required.
        internal static readonly PlateProcessProfile Bromide = new PlateProcessProfile(
            "Bromide", durationSeconds: 3f, sampleCount: 32,
            redSensitivity: 0.30f, greenSensitivity: 0.59f, blueSensitivity: 1.00f,
            developmentStrength: 8.0f, hdGamma: 1.05f);

        internal static PlateProcessProfile ForProcess(PlateProcess process) => process switch
        {
            PlateProcess.Chloride => Chloride,
            PlateProcess.Iodide   => Iodide,
            PlateProcess.Bromide  => Bromide,
            _                     => Iodide
        };

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
