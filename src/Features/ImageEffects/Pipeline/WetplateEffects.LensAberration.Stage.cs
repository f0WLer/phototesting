using System.Runtime.InteropServices;
using SkiaSharp;

namespace Phototesting.ImageEffects
{
    public static partial class WetplateEffects
    {
        // Simulates radial lens aberration (spherical aberration / field curvature).
        // Historical lenses were poorly corrected: the image is sharp at centre, progressively
        // softer toward edges and corners. Chloride-era lenses (Petzval, landscape meniscus)
        // had severe edge softness; iodide wet-plate was improved; bromide used anastigmats.
        //
        // Applied after MicroBlur, before grain.
        private static void ApplyLensAberration(SKBitmap bmp, WetplateEffectsConfig cfg)
        {
            if (cfg.LensAberration < 0.001f) return;

            int w = bmp.Width;
            int h = bmp.Height;
            float cx = w * 0.5f;
            float cy = h * 0.5f;
            float maxRadius = (float)Math.Sqrt(cx * cx + cy * cy);

            float aberrationStart = cfg.LensAberrationStart;
            float sigma = cfg.LensAberrationSigma;
            float intensity = cfg.LensAberration;

            // Build a blurred copy at the configured sigma.
            using var blurredBmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque));
            using (var canvas = new SKCanvas(blurredBmp))
            using (var paint = new SKPaint
            {
                BlendMode = SKBlendMode.Src,
                ImageFilter = SKImageFilter.CreateBlur(sigma, sigma)
            })
            {
                using var srcImg = SKImage.FromBitmap(bmp);
                canvas.DrawImage(srcImg, new SKRect(0, 0, w, h), paint);
            }

            int count = w * h * 4;
            byte[] src = new byte[count];
            byte[] blr = new byte[count];
            Marshal.Copy(bmp.GetPixels(), src, 0, count);
            Marshal.Copy(blurredBmp.GetPixels(), blr, 0, count);

            // Per-pixel radial blend: 0 inside LensAberrationStart, rises smoothly to
            // `intensity` at the corner.
            for (int y = 0; y < h; y++)
            {
                float dy = (y - cy) / maxRadius;
                for (int x = 0; x < w; x++)
                {
                    float dx = (x - cx) / maxRadius;
                    float r = (float)Math.Sqrt(dx * dx + dy * dy); // 0..1+
                    float wt = SmoothStep(aberrationStart, 1.0f, r) * intensity;

                    int i = (y * w + x) * 4;
                    src[i + 0] = (byte)(src[i + 0] * (1f - wt) + blr[i + 0] * wt);
                    src[i + 1] = (byte)(src[i + 1] * (1f - wt) + blr[i + 1] * wt);
                    src[i + 2] = (byte)(src[i + 2] * (1f - wt) + blr[i + 2] * wt);
                }
            }

            Marshal.Copy(src, 0, bmp.GetPixels(), count);
        }
    }
}