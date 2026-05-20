using Vintagestory.API.MathTools;

namespace Phototesting.ImageEffects
{
    public sealed class WetplateEffectsConfig
    {
        public bool Enabled = true;

        // Realism tuning (0..1). Keep subtle; these are meant to break the "perfect filter" feel.
        // Imperfection: biases dust toward edges and adds slight one-sided density pooling.
        public float Imperfection = 0.60f;

        // 0..1: makes edges slightly warmer / more sepia than the center.
        public float EdgeWarmth = 0.12f;

        // 0..1: adds subtle non-uniformity (mottle/banding/density shift) near the top of the frame.
        public float SkyUnevenness = 0.30f;

        // 0..1: small edge-preserving micro blur to soften thin geometry (e.g. leaves) without killing trunks.
        public float MicroBlur = 0.18f;

        // Fraction (0..1) of the image height treated as "sky/top area" for SkyUnevenness.
        public float SkyTopFraction = 0.50f;

        // 0..1 blend of sepia tone over the original.
        public float SepiaStrength = 0.07f;

        // Contrast multiplier (1 = unchanged).
        public float Contrast = 1.32f;

        // 0..1: minimum luminance floor applied after tone mapping.
        // Real wet plates rarely hit true black; this prevents "void blacks" in dark scenes.
        public float ShadowFloor = 0.035f;

        // 0..1: luminance below this is protected from contrast (shadows compress instead of deepen).
        public float ContrastStart = 0.38f;

        // 0..1 shoulder strength for highlights. Higher = more rolloff/compression.
        public float HighlightShoulder = 0.60f;

        // 0..1 start point for shoulder rolloff (where highlight compression begins).
        public float HighlightThreshold = 0.65f;

        // Brightness offset in [-1..1] where 0 = unchanged.
        public float Brightness = 0.065f;

        // 0..1 vignette intensity.
        public float Vignette = 0.24f;

        // Radius multiplier for radial vignette mask.
        public float VignetteRadius = 0.78f;

        // 0..1 sky blowout/bloom strength (applied mainly to the top of the frame).
        public float SkyBlowout = 0.40f;

        // Sky blowout internals.
        public float SkyTopFractionMin = 0.10f;
        public float SkyBlowoutBlurSigmaBase = 0.60f;
        public float SkyBlowoutBlurSigmaScale = 2.20f;
        public float SkyBlowoutLiftScale = 0.06f;
        public float SkyStreakAmount = 0.02f;
        public float SkyStreakFrequency = 2.0f;

        // 0..1 film grain intensity.
        public float Grain = 0.08f;

        // Grain internals.
        public int GrainNoiseMaxDimension = 256;
        public float GrainBlurSigmaBase = 0.6f;
        public float GrainBlurSigmaScale = 1.4f;
        public float GrainToneStart = 0.20f;
        public float GrainToneEnd = 0.90f;
        public float GrainDeltaScale = 0.22f;
        public int GrainNoiseRangeBase = 40;
        public int GrainNoiseRangeScale = 120;
        public int GrainNoiseBaseValue = 128;

        // Decorative artifacts.
        public int DustCount = 80;
        public int ScratchCount = 5;

        // 0..1
        public float DustOpacity = 0.07f;
        public float ScratchOpacity = 0.02f;

        // Dust internals.
        public float DustRadiusMin = 0.4f;
        public float DustRadiusRange = 1.8f;
        public int DustLargeFleckInterval = 37;
        public float DustLargeFleckScale = 2.2f;
        public float DustEdgeBiasChanceScale = 0.65f;
        public float EdgeBiasMaxDistanceFraction = 0.22f;
        public float EdgeBiasDistanceScaleDivisor = 3.0f;

        // Scratch internals.
        public float ScratchAngleMin = -0.15f;
        public float ScratchAngleRange = 0.30f;
        public float ScratchLengthMin = 0.35f;
        public float ScratchLengthRange = 0.85f;
        public float ScratchWidthMin = 0.4f;
        public float ScratchWidthRange = 1.2f;
        public int DarkScratchInterval = 5;
        public float DarkScratchOpacityScale = 0.65f;
        public float DarkScratchWidthScale = 0.7f;
        public float DarkScratchOffsetPx = 1.0f;

        // Sepia + micro-blur internals.
        public float SepiaEdgeWidthMinPx = 4.0f;
        public float SepiaEdgeWidthFraction = 0.18f;
        public float EdgeWarmthBlendScale = 0.60f;
        public float MicroBlurSigmaBase = 0.10f;
        public float MicroBlurSigmaScale = 0.85f;
        public float MicroBlurEdgeKeepScale = 2.2f;

        // Uneven density internals.
        public float PoolingScale = 0.030f;
        public float SkyDensityScale = 0.030f;
        public float SkyMottleScale = 0.020f;
        public float SkyBandScale = 0.010f;
        public int SkyMottleGrid = 24;
        public float SkyDensityTopScale = 0.6f;
        public float SkyMottleTopScale = 0.9f;
        public float SkyBandTopScale = 0.6f;
        public float SkyBandFrequency = 3.0f;
        public float PoolingEdgeBiasCenter = 0.55f;
        public float PoolingEdgeBiasDenominator = 1.10f;

        // Tone curve internals.
        public float ToneSigmoidScale = 6.0f;
        public float HighlightShoulderScale = 12.0f;
        public float ContrastBlendEnd = 0.85f;
        public float ShadowContrastReductionScale = 0.25f;
        public float ContrastStartMin = 0.02f;
        public float ContrastStartMax = 0.75f;

        // Halation: back-scatter glow around bright areas through the glass base.
        // Default 0 = disabled, no change to existing behaviour.
        public float Halation = 0.0f;
        public float HalationThreshold = 0.75f;      // luminance above which glow starts
        public float HalationRadius = 0.018f;         // fraction of max(w,h) used as blur radius
        public float HalationBlurSigmaScale = 2.0f;   // sigma multiplier in the downsampled blur space
        public float HalationTint = 0.0f;             // 0..1: 0 = neutral white glow, 1 = warm reddish glow

        // Radial lens aberration: progressive edge softness from uncorrected historical optics.
        // Default 0 = disabled, no change to existing behaviour.
        public float LensAberration = 0.0f;
        public float LensAberrationStart = 0.55f;     // 0..1 normalised radius where softening begins
        public float LensAberrationSigma = 2.0f;      // max blur sigma at the image corner

        // Per-photo dynamic variation (deterministic from photo id).
        // DynamicScale is a +/- percentage (0.05 => +/-5%) applied to select parameters.
        public bool DynamicEnabled = false;
        public float DynamicScale = 0.05f;

        // Clamps every tunable value into safe runtime bounds.
        internal void ClampInPlace()
        {
            ClampPrimaryVisualSettings();
            ClampSkyBlowoutSettings();
            ClampGrainSettings();
            ClampArtifactSettings();
            ClampSepiaAndMicroBlurSettings();
            ClampUnevenDensitySettings();
            ClampToneCurveInternalSettings();
            ClampHalationAndLensSettings();

            DynamicScale = ClampRange(DynamicScale, 0f, 0.5f);
        }

        // Clamps top-level effect controls and broad visual tuning fields.
        private void ClampPrimaryVisualSettings()
        {
            SepiaStrength = Clamp01(SepiaStrength);
            Vignette = Clamp01(Vignette);
            Grain = Clamp01(Grain);
            DustOpacity = Clamp01(DustOpacity);
            ScratchOpacity = Clamp01(ScratchOpacity);

            HighlightShoulder = Clamp01(HighlightShoulder);
            HighlightThreshold = Clamp01(HighlightThreshold);
            SkyBlowout = Clamp01(SkyBlowout);

            ShadowFloor = Clamp01(ShadowFloor);
            ContrastStart = Clamp01(ContrastStart);

            Imperfection = Clamp01(Imperfection);
            EdgeWarmth = Clamp01(EdgeWarmth);
            SkyUnevenness = Clamp01(SkyUnevenness);
            MicroBlur = Clamp01(MicroBlur);
            SkyTopFraction = Clamp01(SkyTopFraction);

            VignetteRadius = ClampRange(VignetteRadius, 0.2f, 2.0f);

            Contrast = Math.Max(0.2f, Math.Min(2.5f, Contrast));
            Brightness = Math.Max(-0.5f, Math.Min(0.5f, Brightness));

            DustCount = Math.Max(0, Math.Min(3000, DustCount));
            ScratchCount = Math.Max(0, Math.Min(400, ScratchCount));
        }

        // Clamps sky blowout and streak tuning controls.
        private void ClampSkyBlowoutSettings()
        {
            SkyTopFractionMin = ClampRange(SkyTopFractionMin, 0.01f, 1.0f);
            SkyBlowoutBlurSigmaBase = ClampRange(SkyBlowoutBlurSigmaBase, 0f, 10f);
            SkyBlowoutBlurSigmaScale = ClampRange(SkyBlowoutBlurSigmaScale, 0f, 10f);
            SkyBlowoutLiftScale = ClampRange(SkyBlowoutLiftScale, 0f, 1f);
            SkyStreakAmount = ClampRange(SkyStreakAmount, 0f, 1f);
            SkyStreakFrequency = ClampRange(SkyStreakFrequency, 0f, 32f);
        }

        // Clamps film grain generation and modulation settings.
        private void ClampGrainSettings()
        {
            GrainNoiseMaxDimension = Math.Max(8, Math.Min(2048, GrainNoiseMaxDimension));
            GrainBlurSigmaBase = ClampRange(GrainBlurSigmaBase, 0f, 10f);
            GrainBlurSigmaScale = ClampRange(GrainBlurSigmaScale, 0f, 10f);
            GrainToneStart = Clamp01(GrainToneStart);
            GrainToneEnd = Clamp01(GrainToneEnd);
            if (GrainToneEnd < GrainToneStart) GrainToneEnd = GrainToneStart;
            GrainDeltaScale = ClampRange(GrainDeltaScale, 0f, 2f);
            GrainNoiseRangeBase = Math.Max(0, Math.Min(255, GrainNoiseRangeBase));
            GrainNoiseRangeScale = Math.Max(0, Math.Min(255, GrainNoiseRangeScale));
            GrainNoiseBaseValue = Math.Max(0, Math.Min(255, GrainNoiseBaseValue));
        }

        // Clamps dust/scratch artifact generation controls.
        private void ClampArtifactSettings()
        {
            DustRadiusMin = ClampRange(DustRadiusMin, 0f, 16f);
            DustRadiusRange = ClampRange(DustRadiusRange, 0f, 64f);
            DustLargeFleckInterval = Math.Max(1, Math.Min(10000, DustLargeFleckInterval));
            DustLargeFleckScale = ClampRange(DustLargeFleckScale, 0f, 20f);
            DustEdgeBiasChanceScale = ClampRange(DustEdgeBiasChanceScale, 0f, 1f);
            EdgeBiasMaxDistanceFraction = ClampRange(EdgeBiasMaxDistanceFraction, 0f, 1f);
            EdgeBiasDistanceScaleDivisor = ClampRange(EdgeBiasDistanceScaleDivisor, 0.01f, 100f);

            ScratchAngleMin = ClampRange(ScratchAngleMin, -3.14159f, 3.14159f);
            ScratchAngleRange = ClampRange(ScratchAngleRange, 0f, 6.28318f);
            ScratchLengthMin = ClampRange(ScratchLengthMin, 0f, 10f);
            ScratchLengthRange = ClampRange(ScratchLengthRange, 0f, 10f);
            ScratchWidthMin = ClampRange(ScratchWidthMin, 0f, 20f);
            ScratchWidthRange = ClampRange(ScratchWidthRange, 0f, 20f);
            DarkScratchInterval = Math.Max(1, Math.Min(10000, DarkScratchInterval));
            DarkScratchOpacityScale = ClampRange(DarkScratchOpacityScale, 0f, 4f);
            DarkScratchWidthScale = ClampRange(DarkScratchWidthScale, 0f, 4f);
            DarkScratchOffsetPx = ClampRange(DarkScratchOffsetPx, 0f, 64f);
        }

        // Clamps sepia edge behavior and micro-blur internals.
        private void ClampSepiaAndMicroBlurSettings()
        {
            SepiaEdgeWidthMinPx = ClampRange(SepiaEdgeWidthMinPx, 0f, 256f);
            SepiaEdgeWidthFraction = ClampRange(SepiaEdgeWidthFraction, 0f, 2f);
            EdgeWarmthBlendScale = ClampRange(EdgeWarmthBlendScale, 0f, 4f);
            MicroBlurSigmaBase = ClampRange(MicroBlurSigmaBase, 0f, 10f);
            MicroBlurSigmaScale = ClampRange(MicroBlurSigmaScale, 0f, 10f);
            MicroBlurEdgeKeepScale = ClampRange(MicroBlurEdgeKeepScale, 0f, 20f);
        }

        // Clamps uneven-density and pooling internals.
        private void ClampUnevenDensitySettings()
        {
            PoolingScale = ClampRange(PoolingScale, 0f, 1f);
            SkyDensityScale = ClampRange(SkyDensityScale, 0f, 1f);
            SkyMottleScale = ClampRange(SkyMottleScale, 0f, 1f);
            SkyBandScale = ClampRange(SkyBandScale, 0f, 1f);
            SkyMottleGrid = Math.Max(2, Math.Min(256, SkyMottleGrid));
            SkyDensityTopScale = ClampRange(SkyDensityTopScale, 0f, 4f);
            SkyMottleTopScale = ClampRange(SkyMottleTopScale, 0f, 4f);
            SkyBandTopScale = ClampRange(SkyBandTopScale, 0f, 4f);
            SkyBandFrequency = ClampRange(SkyBandFrequency, 0f, 32f);
            PoolingEdgeBiasCenter = ClampRange(PoolingEdgeBiasCenter, -2f, 2f);
            PoolingEdgeBiasDenominator = ClampRange(PoolingEdgeBiasDenominator, 0.01f, 10f);
        }

        // Clamps contrast/tone shaping internal coefficients.
        private void ClampToneCurveInternalSettings()
        {
            ToneSigmoidScale = ClampRange(ToneSigmoidScale, 0f, 32f);
            HighlightShoulderScale = ClampRange(HighlightShoulderScale, 0f, 64f);
            ContrastBlendEnd = Clamp01(ContrastBlendEnd);
            ShadowContrastReductionScale = ClampRange(ShadowContrastReductionScale, 0f, 4f);
            ContrastStartMin = Clamp01(ContrastStartMin);
            ContrastStartMax = Clamp01(ContrastStartMax);
            if (ContrastStartMax < ContrastStartMin) ContrastStartMax = ContrastStartMin;
        }

        // Clamps halation and lens-aberration controls.
        private void ClampHalationAndLensSettings()
        {
            Halation = Clamp01(Halation);
            HalationThreshold = Clamp01(HalationThreshold);
            HalationRadius = ClampRange(HalationRadius, 0f, 0.5f);
            HalationBlurSigmaScale = ClampRange(HalationBlurSigmaScale, 0f, 10f);
            HalationTint = Clamp01(HalationTint);

            LensAberration = Clamp01(LensAberration);
            LensAberrationStart = ClampRange(LensAberrationStart, 0.1f, 1.0f);
            LensAberrationSigma = ClampRange(LensAberrationSigma, 0f, 20f);
        }

        private static float Clamp01(float v) => GameMath.Clamp(v, 0f, 1f);

        // Clamps a value to an explicit inclusive range.
        private static float ClampRange(float v, float min, float max) => GameMath.Clamp(v, min, max);

        // Creates a shallow copy suitable for runtime snapshots and preset duplication.
        public WetplateEffectsConfig Clone()
        {
            return (WetplateEffectsConfig)MemberwiseClone();
        }
    }
}
