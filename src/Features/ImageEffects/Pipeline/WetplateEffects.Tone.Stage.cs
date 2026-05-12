using System.Buffers;

using SkiaSharp;

namespace Phototesting.ImageEffects
{
    public static partial class WetplateEffects
    {
        // Applies contrast curve, highlight shoulder, and shadow-floor lifting in-place.
        private static void ApplyToneCurveAndShoulderInPlace(SKBitmap bmp, WetplateEffectsConfig cfg)
        {
            int w = bmp.Width;
            int h = bmp.Height;
            IntPtr ptr = bmp.GetPixels();
            if (ptr == IntPtr.Zero) return;

            int count = w * h;
            int byteCount = count * 4;
            byte[] bytes = ArrayPool<byte>.Shared.Rent(byteCount);

            try
            {
                System.Runtime.InteropServices.Marshal.Copy(ptr, bytes, 0, byteCount);

                // Contrast is applied mostly above a shadow threshold so shadows compress instead of deepen.
                float contrast = cfg.Contrast;
                float k = cfg.ToneSigmoidScale * contrast; // sigmoid slope
                float y0 = Sigmoid(-0.5f * k);
                float y1 = Sigmoid(0.5f * k);
                float invSpan = 1f / Math.Max(1e-6f, (y1 - y0));

                float t = Clamp01(cfg.HighlightThreshold);
                // Stronger shoulder = bigger 'a'
                float a = 1f + cfg.HighlightShoulderScale * Clamp01(cfg.HighlightShoulder);
                float denom = 1f - (float)Math.Exp(-a * (1f - t));
                if (denom < 1e-6f) denom = 1e-6f;

                float b = cfg.Brightness;

                float shadowFloor = Clamp01(cfg.ShadowFloor);
                float contrastStart = Clamp01(cfg.ContrastStart);
                // Prevent pathological settings.
                if (contrastStart < cfg.ContrastStartMin) contrastStart = cfg.ContrastStartMin;
                if (contrastStart > cfg.ContrastStartMax) contrastStart = cfg.ContrastStartMax;

                // Low-luma path constants are frame-invariant; compute once.
                float kShadow = cfg.ToneSigmoidScale * (1f + (contrast - 1f) * cfg.ShadowContrastReductionScale);
                float y0s = Sigmoid(-0.5f * kShadow);
                float y1s = Sigmoid(0.5f * kShadow);
                float invSpans = 1f / Math.Max(1e-6f, (y1s - y0s));

                for (int i = 0; i < byteCount; i += 4)
                {
                    float bb = bytes[i + 0] / 255f;
                    float gg = bytes[i + 1] / 255f;
                    float rr = bytes[i + 2] / 255f;

                    // Compute pre-curve luminance to decide how much contrast to apply.
                    float lum = 0.299f * rr + 0.587f * gg + 0.114f * bb;
                    float wContrast = SmoothStep(contrastStart, cfg.ContrastBlendEnd, lum);

                    float rrLo = ApplyCurve(rr + b, kShadow, y0s, invSpans);
                    float ggLo = ApplyCurve(gg + b, kShadow, y0s, invSpans);
                    float bbLo = ApplyCurve(bb + b, kShadow, y0s, invSpans);

                    float rrHi = ApplyCurve(rr + b, k, y0, invSpan);
                    float ggHi = ApplyCurve(gg + b, k, y0, invSpan);
                    float bbHi = ApplyCurve(bb + b, k, y0, invSpan);

                    rr = rrLo * (1f - wContrast) + rrHi * wContrast;
                    gg = ggLo * (1f - wContrast) + ggHi * wContrast;
                    bb = bbLo * (1f - wContrast) + bbHi * wContrast;

                    rr = ApplyShoulder(rr, t, a, denom);
                    gg = ApplyShoulder(gg, t, a, denom);
                    bb = ApplyShoulder(bb, t, a, denom);

                    // Shadow floor: lift toward a minimum luminance without washing mid/highs.
                    if (shadowFloor > 0.001f)
                    {
                        float lum2 = 0.299f * rr + 0.587f * gg + 0.114f * bb;
                        if (lum2 < shadowFloor)
                        {
                            float lift = (shadowFloor - lum2);
                            // Add lift uniformly (preserves neutrality), clamp later.
                            rr += lift;
                            gg += lift;
                            bb += lift;
                        }
                    }

                    bytes[i + 0] = (byte)(Clamp01(bb) * 255f);
                    bytes[i + 1] = (byte)(Clamp01(gg) * 255f);
                    bytes[i + 2] = (byte)(Clamp01(rr) * 255f);
                }

                System.Runtime.InteropServices.Marshal.Copy(bytes, 0, ptr, byteCount);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(bytes);
            }
        }

        // Applies the normalized sigmoid tone curve for one channel.
        private static float ApplyCurve(float x, float k, float y0, float invSpan)
        {
            x = Clamp01(x);
            float y = Sigmoid((x - 0.5f) * k);
            y = (y - y0) * invSpan;
            return Clamp01(y);
        }

        private static float Sigmoid(float x) => 1f / (1f + (float)Math.Exp(-x));

        // Compresses highlights above a threshold into an exponential shoulder.
        private static float ApplyShoulder(float x, float threshold, float a, float denom)
        {
            x = Clamp01(x);
            if (x <= threshold) return x;
            float u = x - threshold;
            float num = 1f - (float)Math.Exp(-a * u);
            float mapped = num / denom;
            return threshold + mapped * (1f - threshold);
        }

        // Smoothly blends between 0 and 1 across [a,b].
        private static float SmoothStep(float a, float b, float x)
        {
            if (x <= a) return 0f;
            if (x >= b) return 1f;
            float t = (x - a) / (b - a);
            return t * t * (3f - 2f * t);
        }
    }
}