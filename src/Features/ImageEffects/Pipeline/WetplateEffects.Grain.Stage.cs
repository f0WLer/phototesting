using SkiaSharp;

namespace Phototesting.ImageEffects
{
    public static partial class WetplateEffects
    {
        // Applies blurred monochrome noise as silver-clump style grain weighted toward brighter tones.
        private static void ApplySilverClumpGrain(SKBitmap bmp, Random rng, WetplateEffectsConfig cfg)
        {
            int w = bmp.Width;
            int h = bmp.Height;
            if (w <= 1 || h <= 1) return;

            int nw = Math.Min(cfg.GrainNoiseMaxDimension, w);
            int nh = Math.Min(cfg.GrainNoiseMaxDimension, h);

            using var noiseBmp = new SKBitmap(new SKImageInfo(nw, nh, SKColorType.Bgra8888, SKAlphaType.Opaque));
            FillNoise(noiseBmp, rng, 1f, cfg);

            // Blur noise to form clumps
            float sigma = cfg.GrainBlurSigmaBase + cfg.GrainBlurSigmaScale * cfg.Grain;
            using var noiseImg = SKImage.FromBitmap(noiseBmp);
            using var clumpBmp = new SKBitmap(new SKImageInfo(nw, nh, SKColorType.Bgra8888, SKAlphaType.Opaque));
            using (var canvas = new SKCanvas(clumpBmp))
            using (var paint = new SKPaint { BlendMode = SKBlendMode.Src, ImageFilter = SKImageFilter.CreateBlur(sigma, sigma) })
            {
                canvas.DrawImage(noiseImg, new SKRect(0, 0, nw, nh), paint);
            }

            IntPtr ptr = bmp.GetPixels();
            IntPtr cptr = clumpBmp.GetPixels();
            if (ptr == IntPtr.Zero || cptr == IntPtr.Zero) return;

            int count = w * h;
            byte[] bytes = new byte[count * 4];
            byte[] cl = new byte[nw * nh * 4];
            System.Runtime.InteropServices.Marshal.Copy(ptr, bytes, 0, bytes.Length);
            System.Runtime.InteropServices.Marshal.Copy(cptr, cl, 0, cl.Length);

            float gStrength = Clamp01(cfg.Grain);
            for (int y = 0; y < h; y++)
            {
                int sy = (int)((y / (float)h) * nh);
                if (sy >= nh) sy = nh - 1;
                for (int x = 0; x < w; x++)
                {
                    int sx = (int)((x / (float)w) * nw);
                    if (sx >= nw) sx = nw - 1;

                    int i = (y * w + x) * 4;
                    int j = (sy * nw + sx) * 4;

                    float l = Luma(bytes, i) / 255f;
                    // Bias grain into mid/high tones; keep shadows cleaner.
                    float wgt = SmoothStep(cfg.GrainToneStart, cfg.GrainToneEnd, l) * gStrength;

                    float n = (cl[j + 2] / 255f) - 0.5f; // use R channel
                    // Clumps as density variations (subtle)
                    float delta = n * (cfg.GrainDeltaScale * wgt);
                    float mul = 1f - delta;

                    bytes[i + 0] = (byte)ClampByte(bytes[i + 0] * mul);
                    bytes[i + 1] = (byte)ClampByte(bytes[i + 1] * mul);
                    bytes[i + 2] = (byte)ClampByte(bytes[i + 2] * mul);
                }
            }

            System.Runtime.InteropServices.Marshal.Copy(bytes, 0, ptr, bytes.Length);
        }

        // Computes Rec.601-ish luminance from BGRA bytes.
        private static float Luma(byte[] bgra, int i)
        {
            // BGRA
            float b = bgra[i + 0];
            float g = bgra[i + 1];
            float r = bgra[i + 2];
            return 0.114f * b + 0.587f * g + 0.299f * r;
        }

        // Fills a bitmap with seeded monochrome noise for subsequent grain shaping.
        private static void FillNoise(SKBitmap bmp, Random rng, float strength, WetplateEffectsConfig cfg)
        {
            int w = bmp.Width;
            int h = bmp.Height;
            IntPtr ptr = bmp.GetPixels();
            if (ptr == IntPtr.Zero) return;

            // BGRA premul
            int count = w * h;
            byte[] bytes = new byte[count * 4];

            // Keep it subtle: mid-gray with variation.
            int range = (int)(cfg.GrainNoiseRangeBase + cfg.GrainNoiseRangeScale * strength);
            int baseVal = cfg.GrainNoiseBaseValue;

            for (int i = 0; i < count; i++)
            {
                int n = baseVal + rng.Next(-range, range + 1);
                if (n < 0) n = 0;
                if (n > 255) n = 255;

                int o = i * 4;
                bytes[o + 0] = (byte)n; // B
                bytes[o + 1] = (byte)n; // G
                bytes[o + 2] = (byte)n; // R
                bytes[o + 3] = 255;     // A
            }

            System.Runtime.InteropServices.Marshal.Copy(bytes, 0, ptr, bytes.Length);
        }
    }
}