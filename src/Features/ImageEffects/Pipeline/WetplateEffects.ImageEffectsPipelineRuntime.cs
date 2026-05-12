namespace Phototesting.ImageEffects
{
    public static partial class WetplateEffects
    {
        // Applies deterministic per-photo variance to selected parameters when dynamic mode is enabled.
        private static void ApplyDynamicVariance(WetplateEffectsConfig cfg, string seedKey)
        {
            if (cfg == null) return;

            float scale = cfg.DynamicScale;
            if (scale <= 0f) return;

            var rng = new Random(StableHash((seedKey ?? string.Empty) + "|dyn"));

            cfg.Contrast *= NextScale(rng, scale);
            cfg.Brightness *= NextScale(rng, scale);
            cfg.ShadowFloor *= NextScale(rng, scale);
            cfg.SkyBlowout *= NextScale(rng, scale);
            cfg.Vignette *= NextScale(rng, scale);
            cfg.Imperfection *= NextScale(rng, scale);
            cfg.Grain *= NextScale(rng, scale);
            cfg.DustOpacity *= NextScale(rng, scale);
            cfg.ScratchOpacity *= NextScale(rng, scale);

            cfg.DustCount = (int)Math.Round(cfg.DustCount * NextScale(rng, scale));
            cfg.ScratchCount = (int)Math.Round(cfg.ScratchCount * NextScale(rng, scale));

            cfg.ClampInPlace();
        }

        // Produces a multiplicative scale factor in [1-scale, 1+scale].
        private static float NextScale(Random rng, float scale)
        {
            // scale is +/- percentage. 0.05 => [0.95, 1.05]
            double t = (rng.NextDouble() * 2.0) - 1.0;
            return (float)(1.0 + t * scale);
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        // Clamps a float into byte-channel bounds.
        private static float ClampByte(float v)
        {
            if (v < 0f) return 0f;
            if (v > 255f) return 255f;
            return v;
        }

        // Computes a stable FNV-1a hash for deterministic random seeding.
        private static int StableHash(string s)
        {
            unchecked
            {
                // FNV-1a 32-bit
                uint h = 2166136261u;
                for (int i = 0; i < s.Length; i++)
                {
                    h ^= s[i];
                    h *= 16777619u;
                }
                return (int)h;
            }
        }
    }
}