using System.Buffers;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace Phototesting.ImageEffects
{
    public static partial class WetplateEffects
    {
        // Simulates halation: light scatter back through the glass base re-exposes the emulsion,
        // creating a soft glow around bright areas. Most prominent in wet-plate iodide (no anti-
        // halation backing), moderate in chloride, minimal in bromide (later coating improvements).
        //
        // Applied after tone curve, before sky blowout.
        private static void ApplyHalation(SKBitmap bmp, WetplateEffectsConfig cfg)
        {
            if (cfg.Halation < 0.001f) return;

            int w = bmp.Width;
            int h = bmp.Height;
            float threshold = cfg.HalationThreshold;
            float intensity = cfg.Halation;
            float tint = cfg.HalationTint;

            // Downsample for the blur pass (cheap: ~128..256px on the long side).
            int blurLong = Math.Max(128, Math.Min(256, Math.Max(w, h)));
            float dsScale = (float)blurLong / Math.Max(w, h);
            int bw = Math.Max(1, (int)(w * dsScale));
            int bh = Math.Max(1, (int)(h * dsScale));

            // Downsample source into a scratch bitmap.
            var info = new SKImageInfo(bw, bh, SKColorType.Bgra8888, SKAlphaType.Opaque);
            using var highlightBmp = new SKBitmap(info);
            using (var canvas = new SKCanvas(highlightBmp))
#pragma warning disable CS0618
            using (var paint = new SKPaint { FilterQuality = SKFilterQuality.Low, BlendMode = SKBlendMode.Src })
#pragma warning restore CS0618
            {
                canvas.Clear(SKColors.Black);
                using var srcImg = SKImage.FromBitmap(bmp);
                canvas.DrawImage(srcImg, new SKRect(0, 0, bw, bh), paint);
            }

            // Mask to highlights only; apply optional warm tint.
            int bCount = bw * bh * 4;
            byte[] hPix = ArrayPool<byte>.Shared.Rent(bCount);
            try
            {
                Marshal.Copy(highlightBmp.GetPixels(), hPix, 0, bCount);

                for (int i = 0; i < bCount; i += 4)
                {
                    float b = hPix[i + 0] / 255f;
                    float g = hPix[i + 1] / 255f;
                    float r = hPix[i + 2] / 255f;
                    float lum = 0.299f * r + 0.587f * g + 0.114f * b;

                    // Smooth rolloff above threshold — only bright areas contribute.
                    float above = Clamp01((lum - threshold) / Math.Max(0.01f, 1f - threshold));
                    above = above * above; // sharpen the falloff slightly

                    // Tint: shift R up, B down for a warm/rosy glow at higher tint values.
                    float tr = Clamp01(r + tint * 0.14f);
                    float tg = Clamp01(g - tint * 0.02f);
                    float tb = Clamp01(b - tint * 0.10f);

                    hPix[i + 0] = (byte)(tb * above * 255f);
                    hPix[i + 1] = (byte)(tg * above * 255f);
                    hPix[i + 2] = (byte)(tr * above * 255f);
                    hPix[i + 3] = 255;
                }

                Marshal.Copy(hPix, 0, highlightBmp.GetPixels(), bCount);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(hPix);
            }

            // Blur the highlight layer in downsampled space.
            float sigma = cfg.HalationRadius * blurLong * cfg.HalationBlurSigmaScale;
            sigma = Math.Max(0.5f, sigma);

            using var blurredBmp = new SKBitmap(info);
            using (var canvas = new SKCanvas(blurredBmp))
            using (var paint = new SKPaint
            {
                BlendMode = SKBlendMode.Src,
                ImageFilter = SKImageFilter.CreateBlur(sigma, sigma)
            })
            {
                using var hlImg = SKImage.FromBitmap(highlightBmp);
                canvas.DrawImage(hlImg, new SKRect(0, 0, bw, bh), paint);
            }

            // Upsample blurred glow back to full resolution.
            using var glowBmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque));
            using (var canvas = new SKCanvas(glowBmp))
#pragma warning disable CS0618
            using (var paint = new SKPaint { FilterQuality = SKFilterQuality.Low, BlendMode = SKBlendMode.Src })
#pragma warning restore CS0618
            {
                canvas.Clear(SKColors.Black);
                using var blurredImg = SKImage.FromBitmap(blurredBmp);
                canvas.DrawImage(blurredImg, new SKRect(0, 0, w, h), paint);
            }

            // Screen composite: result = orig + glow*intensity - orig*glow*intensity
            int fullCount = w * h * 4;
            byte[] origPix = ArrayPool<byte>.Shared.Rent(fullCount);
            byte[] glowPix = ArrayPool<byte>.Shared.Rent(fullCount);
            try
            {
                Marshal.Copy(bmp.GetPixels(), origPix, 0, fullCount);
                Marshal.Copy(glowBmp.GetPixels(), glowPix, 0, fullCount);

                for (int i = 0; i < fullCount; i += 4)
                {
                    for (int c = 0; c < 3; c++)
                    {
                        float o = origPix[i + c] / 255f;
                        float gv = glowPix[i + c] / 255f * intensity;
                        origPix[i + c] = (byte)(Clamp01(o + gv - o * gv) * 255f);
                    }
                }

                Marshal.Copy(origPix, 0, bmp.GetPixels(), fullCount);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(origPix);
                ArrayPool<byte>.Shared.Return(glowPix);
            }
        }
    }
}