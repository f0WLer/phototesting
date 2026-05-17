using System.Buffers;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace Phototesting.CameraCapture.Exposure
{
    // HDR floating-point accumulation buffer for multi-frame virtual camera exposure.
    //
    // Accumulate() converts input pixels and adds them to per-channel float sums.
    // Develop() maps those sums to a BGRA8888 bitmap through up to three physics stages,
    // each independently togglable:
    //
    //   LinearizeInput       - sRGB-to-linear conversion before accumulating
    //   ApplySpectralWeights - collapse RGB to a single silver-density value using
    //                          historical emulsion sensitivity (produces grayscale)
    //   ApplyHDCurve         - Hurter-Driffield response curve instead of linear mapping
    //                          (shadows lift, highlights compress, strong photographic shoulder)
    //
    // All three default to true. Set any flag to false to isolate the effect of the others.
    internal sealed class ExposureAccumulationBuffer : ICpuExposureAccumulator
    {
        private readonly int _width;
        private readonly int _height;
        private readonly float[] _sumB;
        private readonly float[] _sumG;
        private readonly float[] _sumR;
        private int _frameCount;
        private readonly int _referenceFrameCount;

        // Precomputed sRGB-to-linear LUT (index = 0-255 sRGB byte value).
        private static readonly float[] SRgbToLinear = BuildLinearTable();

        // Convert sRGB input pixels to linear light before accumulating.
        // Disabling accumulates in gamma space; faster but physically incorrect.
        public bool LinearizeInput { get; set; } = true;

        // Collapse RGB to a single silver-density value using historical spectral weights.
        // Produces grayscale output matching orthochromatic emulsions (blue-sensitive, red-blind).
        // Disabling preserves full colour in the developed output.
        public bool ApplySpectralWeights { get; set; } = true;

        // Wet-plate collodion spectral sensitivity (used when ApplySpectralWeights = true).
        // Weights are normalized internally so a grey pixel always maps to the same energy.
        public float RedSensitivity   { get; set; } = 0.12f;
        public float GreenSensitivity { get; set; } = 0.45f;
        public float BlueSensitivity  { get; set; } = 1.00f;

        // Apply Hurter-Driffield nonlinear emulsion response in Develop().
        // Highlights compress into a natural shoulder instead of hard-clipping to white.
        // Disabling uses the original linear sum/reference mapping.
        public bool ApplyHDCurve { get; set; } = true;

        // H&D curve parameters (used when ApplyHDCurve = true).
        //   DevelopmentStrength: controls how quickly the curve rises; higher = brighter overall
        //   HDGamma:             contrast of the developed image; higher = more contrasty
        // Tuned so the reference exposure lands closer to a usable mid-grey instead of the toe.
        public float DevelopmentStrength { get; set; } = 3.5f;
        public float HDGamma             { get; set; } = 1.1f;

        // When true, Develop() normalises by the actual accumulated frame count rather than the
        // reference count. Use when wall-clock duration controls shutter close (not sample count),
        // so developed brightness is independent of how many samples actually arrived.
        public bool NormalizeByActualFrameCount { get; set; } = false;

        public int FramesAccumulated => _frameCount;
        public int Width  => _width;
        public int Height => _height;

        internal ExposureAccumulationBuffer(int width, int height, int referenceFrameCount)
        {
            _width  = width;
            _height = height;
            _referenceFrameCount = Math.Max(1, referenceFrameCount);
            int count = width * height;
            _sumB = new float[count];
            _sumG = new float[count];
            _sumR = new float[count];
        }

        // Resets the buffer to zero accumulated frames.
        public void Reset()
        {
            Array.Clear(_sumB, 0, _sumB.Length);
            Array.Clear(_sumG, 0, _sumG.Length);
            Array.Clear(_sumR, 0, _sumR.Length);
            _frameCount = 0;
        }

        // Accumulates one BGRA8888 frame into the running channel sums.
        // Applies sRGB-to-linear conversion when LinearizeInput is true.
        // Frames with dimensions other than Width x Height are ignored.
        internal void Accumulate(SKBitmap frame)
        {
            if (frame.Width != _width || frame.Height != _height) return;

            int pixelCount = _width * _height;
            int byteCount  = pixelCount * 4;
            byte[] bytes   = ArrayPool<byte>.Shared.Rent(byteCount);
            try
            {
                Marshal.Copy(frame.GetPixels(), bytes, 0, byteCount);
                AccumulateBytes(bytes, pixelCount);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(bytes);
            }
        }

        // Accumulates a BGRA8888 byte array that is already top-left-origin (e.g. from the GPU
        // downsample blit path). Dimensions must match Width x Height or the call is ignored.
        public void Accumulate(byte[] bgra, int width, int height)
        {
            if (width != _width || height != _height) return;
            AccumulateBytes(bgra, width * height);
        }

        private void AccumulateBytes(byte[] bytes, int pixelCount)
        {
            if (LinearizeInput)
            {
                for (int i = 0; i < pixelCount; i++)
                {
                    int b = i * 4;
                    _sumB[i] += SRgbToLinear[bytes[b + 0]];
                    _sumG[i] += SRgbToLinear[bytes[b + 1]];
                    _sumR[i] += SRgbToLinear[bytes[b + 2]];
                }
            }
            else
            {
                for (int i = 0; i < pixelCount; i++)
                {
                    int b = i * 4;
                    _sumB[i] += bytes[b + 0] / 255f;
                    _sumG[i] += bytes[b + 1] / 255f;
                    _sumR[i] += bytes[b + 2] / 255f;
                }
            }

            _frameCount++;
        }

        // Develops accumulated frames into a new BGRA8888 SKBitmap.
        //
        // Exposure level is always relative to referenceFrameCount:
        //   frames < reference: underexposed (dark)
        //   frames = reference: normally exposed
        //   frames > reference: overexposed (bright, highlights roll off via H&D or hard-clip)
        //
        // With ApplySpectralWeights: output is grayscale (silver density image).
        // With ApplyHDCurve: output can exceed 1.0 for overexposed pixels; ToByte clamps to 255.
        // Returns a black image when no frames have been accumulated.
        // Caller owns and is responsible for disposing the returned bitmap.
        public SKBitmap Develop()
        {
            var info   = new SKImageInfo(_width, _height, SKColorType.Bgra8888, SKAlphaType.Opaque);
            var bitmap = new SKBitmap(info);

            int pixelCount = _width * _height;
            int byteCount  = pixelCount * 4;
            byte[] bytes   = ArrayPool<byte>.Shared.Rent(byteCount);
            try
            {
                float invRef = NormalizeByActualFrameCount
                    ? 1f / Math.Max(_frameCount, 1)
                    : 1f / _referenceFrameCount;

                // Capture flags as locals to avoid repeated field reads in the hot loop.
                bool spectral = ApplySpectralWeights;
                bool hd       = ApplyHDCurve;
                float devStr  = DevelopmentStrength;
                float gamma   = HDGamma;

                // Normalize spectral weights so a neutral grey pixel always maps to
                // the same exposure energy regardless of the sensitivity values.
                float rw = RedSensitivity, gw = GreenSensitivity, bw = BlueSensitivity;
                float wSum = rw + gw + bw;
                if (wSum > 1e-6f) { rw /= wSum; gw /= wSum; bw /= wSum; }

                for (int i = 0; i < pixelCount; i++)
                {
                    int idx = i * 4;

                    if (spectral)
                    {
                        // Collapse RGB to a single silver-density exposure value.
                        float E = (_sumR[i] * rw + _sumG[i] * gw + _sumB[i] * bw) * invRef;
                        byte  v = ToByte(hd ? HDCurve(E, devStr, gamma) : E);
                        bytes[idx + 0] = v;
                        bytes[idx + 1] = v;
                        bytes[idx + 2] = v;
                    }
                    else
                    {
                        // Keep full colour.
                        float Eb = _sumB[i] * invRef;
                        float Eg = _sumG[i] * invRef;
                        float Er = _sumR[i] * invRef;
                        bytes[idx + 0] = ToByte(hd ? HDCurve(Eb, devStr, gamma) : Eb);
                        bytes[idx + 1] = ToByte(hd ? HDCurve(Eg, devStr, gamma) : Eg);
                        bytes[idx + 2] = ToByte(hd ? HDCurve(Er, devStr, gamma) : Er);
                    }

                    bytes[idx + 3] = 255;
                }

                Marshal.Copy(bytes, 0, bitmap.GetPixels(), byteCount);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(bytes);
            }

            return bitmap;
        }

        // Hurter-Driffield emulsion response: log10(1 + E*k)^gamma.
        // Output naturally exceeds 1.0 for overexposed pixels; caller clamps via ToByte.
        private static float HDCurve(float E, float k, float gamma)
        {
            float density = MathF.Log10(1f + E * k);
            return MathF.Pow(MathF.Max(density, 0f), gamma);
        }

        // Precomputes the standard sRGB-to-linear LUT (256 entries).
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

        private static byte ToByte(float v)
        {
            if (v <= 0f) return 0;
            if (v >= 1f) return 255;
            return (byte)(v * 255f + 0.5f);
        }

        public void Dispose() { } // No unmanaged resources; satisfies IDisposable via IExposureAccumulator.
    }
}
