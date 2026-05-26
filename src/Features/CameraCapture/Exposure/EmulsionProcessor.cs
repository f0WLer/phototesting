using System.Buffers;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace Phototesting.CameraCapture.Exposure
{
    /// <summary>
    /// Single-pass emulsion physics for non-accumulation capture paths.
    /// Applies the same three physics stages used by <see cref="ExposureAccumulationBuffer"/> — sRGB linearization,
    /// spectral sensitivity collapse, and H&amp;D characteristic curve — but as a single in-place pass
    /// over a BGRA8888 bitmap rather than across many accumulated frames.
    /// Used by single-frame capture paths (PhotoCaptureRenderer, VirtualCaptureService,
    /// VirtualCameraPreviewRenderer) so all capture paths produce a consistently developed
    /// greyscale plate before the effects pipeline runs.
    /// </summary>
    internal static class EmulsionProcessor
    {
        /// <summary>
        /// Applies emulsion physics to <paramref name="bmp"/> in-place, producing a greyscale developed plate.
        /// The three physics stages are individually gated by <paramref name="linearize"/>, <paramref name="spectral"/>,
        /// and <paramref name="hdCurve"/>; all three default to <see langword="true"/>, matching the accumulator defaults.
        /// </summary>
        internal static void ApplyInPlace(
            SKBitmap bmp,
            PlateProcessProfile process,
            bool linearize = true,
            bool spectral   = true,
            bool hdCurve    = true)
        {
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
                    float bL = linearize ? ExposureUtils.SRgbToLinear[bytes[idx + 0]] : bytes[idx + 0] / 255f;
                    float gL = linearize ? ExposureUtils.SRgbToLinear[bytes[idx + 1]] : bytes[idx + 1] / 255f;
                    float rL = linearize ? ExposureUtils.SRgbToLinear[bytes[idx + 2]] : bytes[idx + 2] / 255f;

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

    }
}
