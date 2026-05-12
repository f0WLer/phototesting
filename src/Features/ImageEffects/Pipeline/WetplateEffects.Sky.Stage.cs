using SkiaSharp;

namespace Phototesting.ImageEffects
{
    public static partial class WetplateEffects
    {
        // Applies top-of-frame bloom and streak modulation to mimic sky overexposure.
        private static void ApplySkyBlowout(SKBitmap bmp, Random rng, WetplateEffectsConfig cfg)
        {
            int w = bmp.Width;
            int h = bmp.Height;
            if (w <= 1 || h <= 1) return;

            float strength = Clamp01(cfg.SkyBlowout);
            float topFrac = Math.Max(cfg.SkyTopFractionMin, Clamp01(cfg.SkyTopFraction));
            int topH = Math.Max(1, (int)(h * topFrac));

            // Blur copy for a soft "bloom" feel.
            float sigma = cfg.SkyBlowoutBlurSigmaBase + cfg.SkyBlowoutBlurSigmaScale * strength;

            using var srcCopy = bmp.Copy();
            using var srcImg = SKImage.FromBitmap(srcCopy);
            using var blurred = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque));
            using (var canvas = new SKCanvas(blurred))
            using (var paint = new SKPaint
            {
                BlendMode = SKBlendMode.Src,
                ImageFilter = SKImageFilter.CreateBlur(sigma, sigma)
            })
            {
                canvas.DrawImage(srcImg, new SKRect(0, 0, w, h), paint);
            }

            IntPtr ptr = bmp.GetPixels();
            IntPtr blurPtr = blurred.GetPixels();
            if (ptr == IntPtr.Zero || blurPtr == IntPtr.Zero) return;

            int count = w * h;
            byte[] bytes = new byte[count * 4];
            byte[] blr = new byte[count * 4];
            System.Runtime.InteropServices.Marshal.Copy(ptr, bytes, 0, bytes.Length);
            System.Runtime.InteropServices.Marshal.Copy(blurPtr, blr, 0, blr.Length);

            float phaseY = (float)(rng.NextDouble() * Math.PI * 2.0);
            float phaseX = (float)(rng.NextDouble() * Math.PI * 2.0);
            float phaseCross = (float)(rng.NextDouble() * Math.PI * 2.0);
            float freqY = Math.Max(0.1f, cfg.SkyStreakFrequency);
            float freqX = 0.45f + freqY * 0.35f;
            float freqCross = 0.2f + freqY * 0.2f;

            for (int y = 0; y < topH; y++)
            {
                float fy = 1f - (y / (float)Math.Max(1, topH - 1)); // 1 at top
                // soft mask
                float mask = fy * fy * (3f - 2f * fy);
                float m = strength * mask;
                float yn = y / Math.Max(1f, topH);
                float baseWave = (float)Math.Sin(yn * (float)(Math.PI * 2.0 * freqY) + phaseY);

                for (int x = 0; x < w; x++)
                {
                    int i = (y * w + x) * 4;
                    float xn = (w <= 1) ? 0f : (x / (float)(w - 1));
                    float xWave = (float)Math.Sin(xn * (float)(Math.PI * 2.0 * freqX) + phaseX);
                    float crossWave = (float)Math.Sin((xn + yn * 0.5f) * (float)(Math.PI * 2.0 * freqCross) + phaseCross);
                    float streak = 1f + cfg.SkyStreakAmount * strength * baseWave * (0.7f + 0.2f * xWave + 0.1f * crossWave);

                    float ob = bytes[i + 0] / 255f;
                    float og = bytes[i + 1] / 255f;
                    float orr = bytes[i + 2] / 255f;

                    float bb = blr[i + 0] / 255f;
                    float bg = blr[i + 1] / 255f;
                    float br = blr[i + 2] / 255f;

                    // Screen blend toward blurred image, plus a tiny exposure lift.
                    ob = 1f - (1f - ob) * (1f - bb);
                    og = 1f - (1f - og) * (1f - bg);
                    orr = 1f - (1f - orr) * (1f - br);

                    float lift = cfg.SkyBlowoutLiftScale * m;
                    bytes[i + 0] = (byte)(Clamp01((bytes[i + 0] / 255f) * (1f - m) + ob * m + lift) * 255f);
                    bytes[i + 1] = (byte)(Clamp01((bytes[i + 1] / 255f) * (1f - m) + og * m + lift) * 255f);
                    bytes[i + 2] = (byte)(Clamp01((bytes[i + 2] / 255f) * (1f - m) + orr * m + lift) * 255f);

                    // apply streak modulation
                    bytes[i + 0] = (byte)ClampByte(bytes[i + 0] * streak);
                    bytes[i + 1] = (byte)ClampByte(bytes[i + 1] * streak);
                    bytes[i + 2] = (byte)ClampByte(bytes[i + 2] * streak);
                }
            }

            System.Runtime.InteropServices.Marshal.Copy(bytes, 0, ptr, bytes.Length);
        }

        // Applies directional pooling and low-frequency sky density variation.
        private static void ApplyUnevenDensity(SKBitmap bmp, Random rng, WetplateEffectsConfig cfg)
        {
            int w = bmp.Width;
            int h = bmp.Height;
            IntPtr ptr = bmp.GetPixels();
            if (ptr == IntPtr.Zero) return;

            int count = w * h;
            byte[] bytes = new byte[count * 4];
            System.Runtime.InteropServices.Marshal.Copy(ptr, bytes, 0, bytes.Length);

            // One-sided pooling direction (stable per photo via rng).
            double ang = rng.NextDouble() * Math.PI * 2.0;
            float dx = (float)Math.Cos(ang);
            float dy = (float)Math.Sin(ang);
            float poolAmp = cfg.PoolingScale * cfg.Imperfection;
            float skyAmp = cfg.SkyDensityScale * cfg.SkyUnevenness;
            float mottleAmp = cfg.SkyMottleScale * cfg.SkyUnevenness;
            float bandAmp = cfg.SkyBandScale * cfg.SkyUnevenness;

            // Simple low-frequency noise grid for sky mottling.
            int grid = cfg.SkyMottleGrid;
            float[,] noise = new float[grid + 1, grid + 1];
            for (int gy = 0; gy <= grid; gy++)
                for (int gx = 0; gx <= grid; gx++)
                    noise[gx, gy] = (float)(rng.NextDouble() * 2.0 - 1.0);

            float topFrac = Math.Max(cfg.SkyTopFractionMin, cfg.SkyTopFraction);
            float topH = h * topFrac;
            float phase = (float)(rng.NextDouble() * Math.PI * 2.0);

            for (int y = 0; y < h; y++)
            {
                float ny = (h <= 1) ? 0f : (y / (float)(h - 1));
                for (int x = 0; x < w; x++)
                {
                    float nx = (w <= 1) ? 0f : (x / (float)(w - 1));
                    int i = (y * w + x) * 4;

                    float b = bytes[i + 0];
                    float g = bytes[i + 1];
                    float r = bytes[i + 2];

                    // Subtle one-sided density pooling: darker on one edge.
                    float t = (nx - 0.5f) * dx + (ny - 0.5f) * dy; // -0.5..0.5-ish
                    float edgeT = Clamp01((cfg.PoolingEdgeBiasCenter - t) / cfg.PoolingEdgeBiasDenominator);
                    // Smoothstep
                    edgeT = edgeT * edgeT * (3f - 2f * edgeT);
                    float density = 1f - poolAmp * edgeT;

                    // Sky unevenness: only top region.
                    if (cfg.SkyUnevenness > 0.001f && y < topH)
                    {
                        float yTop = 1f - (y / Math.Max(1f, topH)); // 1 at top -> 0 at cutoff
                        // Vertical density shift (slightly more dense near top)
                        density *= 1f - skyAmp * (cfg.SkyDensityTopScale * yTop);

                        // Mottle (value noise)
                        float u = nx * grid;
                        float v = (ny / topFrac) * grid;
                        int x0 = (int)Math.Floor(u);
                        int y0 = (int)Math.Floor(v);
                        float fu = u - x0;
                        float fv = v - y0;
                        x0 = Math.Max(0, Math.Min(grid - 1, x0));
                        y0 = Math.Max(0, Math.Min(grid - 1, y0));
                        float n00 = noise[x0, y0];
                        float n10 = noise[x0 + 1, y0];
                        float n01 = noise[x0, y0 + 1];
                        float n11 = noise[x0 + 1, y0 + 1];
                        float n0 = n00 + (n10 - n00) * fu;
                        float n1 = n01 + (n11 - n01) * fu;
                        float n = n0 + (n1 - n0) * fv;

                        density *= 1f - mottleAmp * n * (cfg.SkyMottleTopScale * yTop);

                        // Very faint banding
                        float band = (float)Math.Sin((ny / topFrac) * (float)(Math.PI * 2.0 * cfg.SkyBandFrequency) + phase);
                        density *= 1f - bandAmp * band * (cfg.SkyBandTopScale * yTop);
                    }

                    // Apply density multiplier
                    r *= density;
                    g *= density;
                    b *= density;

                    bytes[i + 0] = (byte)ClampByte(b);
                    bytes[i + 1] = (byte)ClampByte(g);
                    bytes[i + 2] = (byte)ClampByte(r);
                }
            }

            System.Runtime.InteropServices.Marshal.Copy(bytes, 0, ptr, bytes.Length);
        }
    }
}