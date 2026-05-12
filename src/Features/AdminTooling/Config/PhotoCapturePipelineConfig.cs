namespace Phototesting.AdminTooling
{
    // Capture-pipeline processing knobs shared by photo capture and preview paths.
    // Keeps blank-frame and png-encode tuning centralized.
    public sealed class PhotoCapturePipelineConfig
    {
        /// <summary>Blank-frame detection sample density divisor. Lower = less CPU and fewer samples; higher = more CPU and stronger detection.</summary>
        public int BlankDetectSampleDivisor = 32;

        /// <summary>PNG compression quality parameter. Lower = faster encode/larger files; higher = slower encode/smaller files.</summary>
        public int PngCompressionQuality = 90;

        // Clamps capture pipeline values to safe bounds for predictable runtime cost.
        internal void ClampInPlace()
        {
            if (BlankDetectSampleDivisor < 4) BlankDetectSampleDivisor = 4;
            if (BlankDetectSampleDivisor > 4096) BlankDetectSampleDivisor = 4096;

            if (PngCompressionQuality < 0) PngCompressionQuality = 0;
            if (PngCompressionQuality > 100) PngCompressionQuality = 100;
        }
    }
}
