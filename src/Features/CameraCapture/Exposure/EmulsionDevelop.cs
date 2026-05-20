using System.Buffers;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace Phototesting.CameraCapture.Exposure
{
    // Single-pass emulsion physics for non-accumulation capture paths.
    //
    // Applies the same three physics stages that ExposureAccumulationBuffer uses
    // during frame accumulation and development, but collapses them into one in-place
    // pass over a single BGRA8888 bitmap:
    //
    //   1. sRGB linearization        — same LUT as ExposureAccumulationBuffer
    //   2. Spectral sensitivity       — per-channel weights from PlateProcessProfile collapse to silver density
    //   3. H&D response curve         — log10-based characteristic curve, same math as Develop()
    //
    // Used by single-frame capture paths (PhotoCaptureRenderer, VirtualCaptureService,
    // VirtualCameraPreviewRenderer) so all capture paths produce a consistently developed
    // greyscale plate before the effects pipeline runs.
    //
    // The exposure accumulator paths (VirtualExposureRenderer) are unaffected;
    // they continue to do this work inside the accumulator's Develop() method.
    internal static class EmulsionDevelop
    {
        // Precomputed sRGB-to-linear LUT (index = 0-255 sRGB byte value).
        // Identical to the table in ExposureAccumulationBuffer — shared math, separate instance.
        private static readonly float[] SRgbToLinear = BuildLinearTable();

        // Applies emulsion physics to a BGRA8888 bitmap in-place, producing a greyscale developed plate.
        // The three physics stages are individually gated; pass false to skip a stage.
        // All three default to on — matching the accumulator's default physics settings.
        internal static void ApplyInPlace(
            SKBitmap bmp,
            PlateProcessProfile process,
            bool linearize = true,
            bool spectral   = true,
            bool hdCurve    = true)
        {
            if (bmp == null) return;

            int count     = bmp.Width * bmp.Height;
            int byteCount = count * 4;

            byte[] bytes = ArrayPool<byte>.Shared.Rent(byteCount);
            try
            {
                Marshal.Copy(bmp.GetPixels(), bytes, 0, byteCount);

                // Normalise spectral weights so a neutral grey pixel always maps to the same density.
                float rw = process.RedSensitivity;
                float gw = process.GreenSensitivity;
                float bw = process.BlueSensitivity;
                float wSum = rw + gw + bw;
                if (wSum > 1e-6f) { rw /= wSum; gw /= wSum; bw /= wSum; }

                float devStr = process.DevelopmentStrength;
                float gamma  = process.HDGamma;

                for (int i = 0; i < count; i++)
                {
                    int idx = i * 4;

                    // Read raw channel bytes (BGRA layout).
                    float bL = linearize ? SRgbToLinear[bytes[idx + 0]] : bytes[idx + 0] / 255f;
                    float gL = linearize ? SRgbToLinear[bytes[idx + 1]] : bytes[idx + 1] / 255f;
                    float rL = linearize ? SRgbToLinear[bytes[idx + 2]] : bytes[idx + 2] / 255f;

                    // Spectral collapse: weight each channel by emulsion sensitivity → single exposure value.
                    // Without spectral weights, fall back to standard Rec.601 luminance.
                    float E = spectral
                        ? rL * rw + gL * gw + bL * bw
                        : 0.299f * rL + 0.587f * gL + 0.114f * bL;

                    // H&D characteristic curve: log10(1 + E * k)^gamma.
                    float v = hdCurve
                        ? MathF.Pow(MathF.Max(MathF.Log10(1f + E * devStr), 0f), gamma)
                        : E;

                    byte bv = ToByte(v);
                    bytes[idx + 0] = bv;
                    bytes[idx + 1] = bv;
                    bytes[idx + 2] = bv;
                    // alpha unchanged
                }

                Marshal.Copy(bytes, 0, bmp.GetPixels(), byteCount);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(bytes);
            }
        }

        private static byte ToByte(float v)
        {
            if (v <= 0f) return 0;
            if (v >= 1f) return 255;
            return (byte)(v * 255f + 0.5f);
        }

        private static float[] BuildLinearTable()
        {
            float[] t = new float[256];
            for (int i = 0; i < 256; i++)
            {
                float c = i / 255f;
                t[i] = c <= 0.04045f
                    ? c / 12.92f
                    : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
            }
            return t;
        }
    }
}
