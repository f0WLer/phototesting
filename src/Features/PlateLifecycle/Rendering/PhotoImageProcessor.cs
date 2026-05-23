using SkiaSharp;
using Vintagestory.API.Client;

namespace Phototesting.PlateLifecycle.Rendering
{
    internal static class PhotoImageProcessor
    {
        // Reads PNG width/height directly from IHDR bytes without full image decode.
        internal static bool TryGetPngDimensions(byte[] pngBytes, out int width, out int height)
        {
            width = 0;
            height = 0;

            if (pngBytes == null || pngBytes.Length < 24) return false;

            if (pngBytes[0] != 0x89 || pngBytes[1] != 0x50 || pngBytes[2] != 0x4E || pngBytes[3] != 0x47
                || pngBytes[4] != 0x0D || pngBytes[5] != 0x0A || pngBytes[6] != 0x1A || pngBytes[7] != 0x0A)
            {
                return false;
            }

            try
            {
                width = (pngBytes[16] << 24) | (pngBytes[17] << 16) | (pngBytes[18] << 8) | pngBytes[19];
                height = (pngBytes[20] << 24) | (pngBytes[21] << 16) | (pngBytes[22] << 8) | pngBytes[23];
            }
            catch
            {
                width = 0;
                height = 0;
                return false;
            }

            return width > 0 && height > 0;
        }

        // Ensures a derived photo variant exists and is up-to-date with source/effect inputs.
        internal static bool TryEnsureDerivedPhoto(ICoreClientAPI capi, string sourcePath, string derivedPath, bool useDevelopedStage, int developPours, int maxDeveloperPours)
        {
            try
            {
                // Reuse existing derived image when source has not changed.
                if (File.Exists(derivedPath))
                {
                    try
                    {
                        DateTime srcTime = File.GetLastWriteTimeUtc(sourcePath);
                        DateTime dstTime = File.GetLastWriteTimeUtc(derivedPath);
                        if (dstTime >= srcTime) return true;
                    }
                    catch
                    {
                        // If time checks fail, fall through and re-generate.
                    }
                }

                using var src = SKBitmap.Decode(sourcePath);
                if (src == null) return false;

                float t = maxDeveloperPours <= 1 ? 1f : (developPours - 1) / (float)(maxDeveloperPours - 1);
                if (t < 0f) t = 0f;
                if (t > 1f) t = 1f;

                if (useDevelopedStage && t < 0.999f)
                {
                    ApplyDevelopmentStageVisuals(src, t);
                }

                using var image = SKImage.FromBitmap(src);
                using var data = image.Encode(SKEncodedImageFormat.Png, 90);

                Directory.CreateDirectory(Path.GetDirectoryName(derivedPath)!);
                File.WriteAllBytes(derivedPath, data.ToArray());
                return true;
            }
            catch (Exception ex)
            {
                capi.Logger.Warning($"Phototesting: failed to build derived photo '{derivedPath}': {ex.Message}");
                return false;
            }
        }

        // Applies per-stage underdeveloped visual treatment directly to bitmap pixels.
        private static void ApplyDevelopmentStageVisuals(SKBitmap bmp, float t)
        {
            if (bmp == null) return;
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;

            // Underdeveloped look at t=0.
            float opacity = Lerp(0.15f, 1f, t);
            float contrast = Lerp(0.35f, 1f, t);
            float whiteHaze = Lerp(0.75f, 0f, t);
            float blackPoint = Lerp(0.35f, 0f, t);
            float edgeFade = Lerp(0.6f, 0f, t);

            int w = bmp.Width;
            int h = bmp.Height;
            if (w <= 0 || h <= 0) return;

            // Important: SKBitmap.GetPixel/SetPixel are extremely slow for full-image processing.
            // The development stages (pours 1-4) can get hit right when the tray mesh rebuilds,
            // so do this via raw pixel access to avoid visible hitching.
            SKPixmap pixmap = bmp.PeekPixels();
            if (pixmap == null)
            {
                ApplyDevelopmentStageVisualsSlow(bmp, w, h, blackPoint, contrast, whiteHaze, edgeFade, opacity);
                return;
            }

            SKColorType ct = pixmap.ColorType;
            int bpp = pixmap.BytesPerPixel;
            if (bpp != 4 || (ct != SKColorType.Bgra8888 && ct != SKColorType.Rgba8888))
            {
                ApplyDevelopmentStageVisualsSlow(bmp, w, h, blackPoint, contrast, whiteHaze, edgeFade, opacity);
                return;
            }

            bool bgra = ct == SKColorType.Bgra8888;
            bool doEdgeFade = edgeFade > 0f;

            float invW = 1f / w;
            float invH = 1f / h;
            float invCorner = 1f / 0.7071f;

            float[] nx2 = new float[w];
            for (int x = 0; x < w; x++)
            {
                float nx = (x + 0.5f) * invW - 0.5f;
                nx2[x] = nx * nx;
            }

            float[] ny2 = new float[h];
            for (int y = 0; y < h; y++)
            {
                float ny = (y + 0.5f) * invH - 0.5f;
                ny2[y] = ny * ny;
            }

            unsafe
            {
                byte* basePtr = (byte*)pixmap.GetPixels().ToPointer();
                int rowBytes = pixmap.RowBytes;

                for (int y = 0; y < h; y++)
                {
                    byte* row = basePtr + y * rowBytes;
                    float yTerm = ny2[y];

                    for (int x = 0; x < w; x++)
                    {
                        int i = x * 4;
                        byte r8;
                        byte g8;
                        byte b8;

                        if (bgra)
                        {
                            b8 = row[i + 0];
                            g8 = row[i + 1];
                            r8 = row[i + 2];
                        }
                        else
                        {
                            r8 = row[i + 0];
                            g8 = row[i + 1];
                            b8 = row[i + 2];
                        }

                        float r = r8 / 255f;
                        float g = g8 / 255f;
                        float b = b8 / 255f;

                        float edgeFadeAmount = 0f;
                        if (doEdgeFade)
                        {
                            float edge = (float)System.Math.Sqrt(nx2[x] + yTerm) * invCorner;
                            if (edge > 1f) edge = 1f;
                            edgeFadeAmount = edge * edgeFade;
                        }

                        ApplyDevelopmentStageTransform(ref r, ref g, ref b, blackPoint, contrast, whiteHaze, edgeFadeAmount, opacity);

                        byte rr = (byte)(r * 255f);
                        byte gg = (byte)(g * 255f);
                        byte bb = (byte)(b * 255f);

                        if (bgra)
                        {
                            row[i + 0] = bb;
                            row[i + 1] = gg;
                            row[i + 2] = rr;
                            row[i + 3] = 255;
                        }
                        else
                        {
                            row[i + 0] = rr;
                            row[i + 1] = gg;
                            row[i + 2] = bb;
                            row[i + 3] = 255;
                        }
                    }
                }
            }
        }

        // Slow fallback path for color formats where direct pixel pointer edits are unsafe.
        private static void ApplyDevelopmentStageVisualsSlow(SKBitmap bmp, int w, int h, float blackPoint, float contrast, float whiteHaze, float edgeFade, float opacity)
        {
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    var c = bmp.GetPixel(x, y);

                    float r = c.Red / 255f;
                    float g = c.Green / 255f;
                    float b = c.Blue / 255f;

                    float edgeFadeAmount = 0f;
                    if (edgeFade > 0f)
                    {
                        float nx = (x + 0.5f) / w - 0.5f;
                        float ny = (y + 0.5f) / h - 0.5f;
                        float dist = (float)System.Math.Sqrt(nx * nx + ny * ny);
                        float edge = dist / 0.7071f;
                        if (edge > 1f) edge = 1f;
                        edgeFadeAmount = edge * edgeFade;
                    }

                    ApplyDevelopmentStageTransform(ref r, ref g, ref b, blackPoint, contrast, whiteHaze, edgeFadeAmount, opacity);

                    byte rr = (byte)(r * 255f);
                    byte gg = (byte)(g * 255f);
                    byte bb = (byte)(b * 255f);

                    bmp.SetPixel(x, y, new SKColor(rr, gg, bb, 255));
                }
            }
        }

        // Transforms one pixel through black point, contrast, haze, edge fade, and opacity blend.
        private static void ApplyDevelopmentStageTransform(ref float r, ref float g, ref float b, float blackPoint, float contrast, float whiteHaze, float edgeFadeAmount, float opacity)
        {
            r = blackPoint + r * (1f - blackPoint);
            g = blackPoint + g * (1f - blackPoint);
            b = blackPoint + b * (1f - blackPoint);

            r = 0.5f + (r - 0.5f) * contrast;
            g = 0.5f + (g - 0.5f) * contrast;
            b = 0.5f + (b - 0.5f) * contrast;

            r = r + (1f - r) * whiteHaze;
            g = g + (1f - g) * whiteHaze;
            b = b + (1f - b) * whiteHaze;

            if (edgeFadeAmount > 0f)
            {
                r = r + (1f - r) * edgeFadeAmount;
                g = g + (1f - g) * edgeFadeAmount;
                b = b + (1f - b) * edgeFadeAmount;
            }

            r = r + (1f - r) * (1f - opacity);
            g = g + (1f - g) * (1f - opacity);
            b = b + (1f - b) * (1f - opacity);

            if (r < 0f) r = 0f; else if (r > 1f) r = 1f;
            if (g < 0f) g = 0f; else if (g > 1f) g = 1f;
            if (b < 0f) b = 0f; else if (b > 1f) b = 1f;
        }

        // Clamped linear interpolation helper.
        private static float Lerp(float a, float b, float t)
        {
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;
            return a + (b - a) * t;
        }

        // Clamps values into 0..1 range.
        private static float Clamp01(float v)
        {
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }
    }
}