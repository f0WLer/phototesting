using System.Buffers;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace Phototesting.ImageEffects
{
    public static partial class WetplateEffects
    {
        // Fast path: skip the entire pass when all curves are at linear defaults.
        private static bool AnyChannelCurveActive(WetplateEffectsConfig cfg)
        {
            return !(
                cfg.CurveRedToe < 0.001f && IsMidLinear(cfg.CurveRedMid) && cfg.CurveRedShoulder > 0.999f &&
                cfg.CurveGreenToe < 0.001f && IsMidLinear(cfg.CurveGreenMid) && cfg.CurveGreenShoulder > 0.999f &&
                cfg.CurveBlueToe < 0.001f && IsMidLinear(cfg.CurveBlueMid) && cfg.CurveBlueShoulder > 0.999f
            );
        }

        private static bool IsMidLinear(float mid) => mid > 0.499f && mid < 0.501f;

        // Build 256-entry LUT for a quadratic Bezier through (0,toe), (0.5,mid), (1,shoulder).
        // B(t) = (1-t)^2 * toe  +  2*(1-t)*t * mid  +  t^2 * shoulder,  t = input/255
        private static byte[] BuildCurveLut(float toe, float mid, float shoulder)
        {
            var lut = new byte[256];
            for (int i = 0; i < 256; i++)
            {
                float t = i / 255f;
                float oneMinusT = 1f - t;
                float y = oneMinusT * oneMinusT * toe
                        + 2f * oneMinusT * t * mid
                        + t * t * shoulder;
                lut[i] = (byte)(Clamp01(y) * 255f);
            }
            return lut;
        }

        // Applied as step 0: before channel bias and greyscale conversion.
        // Allows independent non-linear shaping of each colour channel's response,
        // simulating different film/emulsion exposure latitude per channel.
        private static void ApplyChannelCurvesInPlace(SKBitmap bmp, WetplateEffectsConfig cfg)
        {
            if (!AnyChannelCurveActive(cfg)) return;

            var lutR = BuildCurveLut(cfg.CurveRedToe, cfg.CurveRedMid, cfg.CurveRedShoulder);
            var lutG = BuildCurveLut(cfg.CurveGreenToe, cfg.CurveGreenMid, cfg.CurveGreenShoulder);
            var lutB = BuildCurveLut(cfg.CurveBlueToe, cfg.CurveBlueMid, cfg.CurveBlueShoulder);

            int count = bmp.Width * bmp.Height * 4;
            byte[] pixels = ArrayPool<byte>.Shared.Rent(count);
            try
            {
                Marshal.Copy(bmp.GetPixels(), pixels, 0, count);

                // BGRA layout: [0]=B, [1]=G, [2]=R, [3]=A
                for (int i = 0; i < count; i += 4)
                {
                    pixels[i + 0] = lutB[pixels[i + 0]];
                    pixels[i + 1] = lutG[pixels[i + 1]];
                    pixels[i + 2] = lutR[pixels[i + 2]];
                    // alpha unchanged
                }

                Marshal.Copy(pixels, 0, bmp.GetPixels(), count);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(pixels);
            }
        }
    }
}