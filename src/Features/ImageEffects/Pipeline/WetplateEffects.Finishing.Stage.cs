using SkiaSharp;

namespace Phototesting.ImageEffects
{
    public static partial class WetplateEffects
    {
        // Applies sepia toning with a slight edge warmth boost near plate borders.
        private static void ApplySepiaAtEnd(SKBitmap bmp, WetplateEffectsConfig cfg)
        {
            int w = bmp.Width;
            int h = bmp.Height;
            IntPtr ptr = bmp.GetPixels();
            if (ptr == IntPtr.Zero) return;

            int count = w * h;
            byte[] bytes = new byte[count * 4];
            System.Runtime.InteropServices.Marshal.Copy(ptr, bytes, 0, bytes.Length);

            float s = Clamp01(cfg.SepiaStrength);
            float edgeWidthPx = Math.Max(cfg.SepiaEdgeWidthMinPx, Math.Min(w, h) * cfg.SepiaEdgeWidthFraction);
            float edgeWarm = Clamp01(cfg.EdgeWarmth);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = (y * w + x) * 4;
                    float b = bytes[i + 0] / 255f;
                    float g = bytes[i + 1] / 255f;
                    float r = bytes[i + 2] / 255f;

                    // Basic sepia tone (normalized)
                    float sr = Clamp01(r * 0.393f + g * 0.769f + b * 0.189f);
                    float sg = Clamp01(r * 0.349f + g * 0.686f + b * 0.168f);
                    float sb = Clamp01(r * 0.272f + g * 0.534f + b * 0.131f);

                    // Edge warmth: increase sepia blend slightly at edges
                    float distToEdge = Math.Min(Math.Min(x, w - 1 - x), Math.Min(y, h - 1 - y));
                    float edge = Clamp01(1f - (distToEdge / edgeWidthPx));
                    edge = edge * edge * (3f - 2f * edge);
                    float blend = Clamp01(s * (1f + cfg.EdgeWarmthBlendScale * edgeWarm * edge));

                    r = r * (1f - blend) + sr * blend;
                    g = g * (1f - blend) + sg * blend;
                    b = b * (1f - blend) + sb * blend;

                    bytes[i + 0] = (byte)(b * 255f);
                    bytes[i + 1] = (byte)(g * 255f);
                    bytes[i + 2] = (byte)(r * 255f);
                }
            }

            System.Runtime.InteropServices.Marshal.Copy(bytes, 0, ptr, bytes.Length);
        }

        // Applies a very small blur while preserving strong edges to avoid global smearing.
        private static void ApplyEdgePreservingMicroBlur(SKBitmap bmp, float amount, WetplateEffectsConfig cfg)
        {
            int w = bmp.Width;
            int h = bmp.Height;
            if (w < 3 || h < 3) return;

            // Keep it tiny: enough to soften leaves, not enough to smear the whole image.
            float sigma = cfg.MicroBlurSigmaBase + cfg.MicroBlurSigmaScale * Clamp01(amount);

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

            IntPtr srcPtr = bmp.GetPixels();
            IntPtr blurPtr = blurred.GetPixels();
            if (srcPtr == IntPtr.Zero || blurPtr == IntPtr.Zero) return;

            int count = w * h;
            byte[] src = new byte[count * 4];
            byte[] blr = new byte[count * 4];
            System.Runtime.InteropServices.Marshal.Copy(srcPtr, src, 0, src.Length);
            System.Runtime.InteropServices.Marshal.Copy(blurPtr, blr, 0, blr.Length);

            // Edge strength threshold: higher keeps trunks/strong edges sharp.
            float edgeK = cfg.MicroBlurEdgeKeepScale;

            // Skip border pixels.
            for (int y = 1; y < h - 1; y++)
            {
                for (int x = 1; x < w - 1; x++)
                {
                    int i = (y * w + x) * 4;

                    float yL = Luma(src, ((y * w + (x - 1)) * 4));
                    float yR = Luma(src, ((y * w + (x + 1)) * 4));
                    float yU = Luma(src, (((y - 1) * w + x) * 4));
                    float yD = Luma(src, (((y + 1) * w + x) * 4));

                    float dx = yR - yL;
                    float dy = yD - yU;
                    float edge = (float)Math.Sqrt(dx * dx + dy * dy) / 255f;

                    // weight=1 keeps original; weight=0 uses blurred.
                    float wKeep = Clamp01(edge * edgeK);

                    // Lerp: blurred -> original
                    src[i + 0] = (byte)ClampByte(blr[i + 0] + (src[i + 0] - blr[i + 0]) * wKeep);
                    src[i + 1] = (byte)ClampByte(blr[i + 1] + (src[i + 1] - blr[i + 1]) * wKeep);
                    src[i + 2] = (byte)ClampByte(blr[i + 2] + (src[i + 2] - blr[i + 2]) * wKeep);
                    // alpha untouched (opaque)
                }
            }

            System.Runtime.InteropServices.Marshal.Copy(src, 0, srcPtr, src.Length);
        }

    }
}