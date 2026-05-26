using System.Buffers;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace Phototesting.CameraCapture.Exposure
{
    /// <summary>
    /// HDR floating-point accumulation buffer for multi-frame virtual camera exposure.
    /// Frames are ingested via <see cref="Accumulate"/> and developed into a BGRA8888 bitmap via <see cref="Develop"/>.
    /// Three physics stages are independently togglable on both paths:
    /// sRGB linearization (<see cref="LinearizeInput"/>), spectral sensitivity collapse (<see cref="ApplySpectralWeights"/>),
    /// and Hurter-Driffield characteristic curve (<see cref="ApplyHDCurve"/>).
    /// </summary>
    internal sealed class ExposureAccumulationBuffer : ICpuExposureAccumulator
    {
        private readonly int _width;
        private readonly int _height;
        private readonly float[] _sumB;
        private readonly float[] _sumG;
        private readonly float[] _sumR;
        private int _frameCount;
        private readonly int _targetSampleCount;

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
            _targetSampleCount = Math.Max(1, referenceFrameCount);
            int count = width * height;
            _sumB = new float[count];
            _sumG = new float[count];
            _sumR = new float[count];
        }

        /// <summary>Resets all per-channel float sums and the frame counter to zero.</summary>
        public void Reset()
        {
            Array.Clear(_sumB, 0, _sumB.Length);
            Array.Clear(_sumG, 0, _sumG.Length);
            Array.Clear(_sumR, 0, _sumR.Length);
            _frameCount = 0;
        }

        /// <summary>
        /// Accumulates a BGRA8888 byte array that has already been read back from the GPU.
        /// The array must be top-left-origin (produced by the GPU downsample blit path).
        /// Calls with dimensions that do not match <see cref="Width"/> × <see cref="Height"/> are silently ignored.
        /// </summary>
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
                    _sumB[i] += ExposureUtils.SRgbToLinear[bytes[b + 0]];
                    _sumG[i] += ExposureUtils.SRgbToLinear[bytes[b + 1]];
                    _sumR[i] += ExposureUtils.SRgbToLinear[bytes[b + 2]];
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

        /// <summary>
        /// Develops all accumulated frames into a new BGRA8888 <see cref="SKBitmap"/>.
        /// Exposure level is relative to the reference frame count: fewer frames produces underexposure,
        /// more produces overexposure (highlights roll off via the H&amp;D curve or hard-clip without it).
        /// Returns a black image when no frames have been accumulated.
        /// The caller owns and must dispose the returned bitmap.
        /// </summary>
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
                    : 1f / _targetSampleCount;

                // Capture flags as locals to avoid repeated field reads in the hot loop.
                bool applySpectral = ApplySpectralWeights;
                bool applyHdCurve  = ApplyHDCurve;
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

                    if (applySpectral)
                    {
                        // Collapse RGB to a single silver-density exposure value.
                        float E = (_sumR[i] * rw + _sumG[i] * gw + _sumB[i] * bw) * invRef;
                        byte  v = ToByte(applyHdCurve ? HDCurve(E, devStr, gamma) : E);
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
                        bytes[idx + 0] = ToByte(applyHdCurve ? HDCurve(Eb, devStr, gamma) : Eb);
                        bytes[idx + 1] = ToByte(applyHdCurve ? HDCurve(Eg, devStr, gamma) : Eg);
                        bytes[idx + 2] = ToByte(applyHdCurve ? HDCurve(Er, devStr, gamma) : Er);
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


        private static byte ToByte(float v)
        {
            if (v <= 0f) return 0;
            if (v >= 1f) return 255;
            return (byte)(v * 255f + 0.5f);
        }

        public byte[]? SerializeAccumulation()
        {
            if (_frameCount <= 0) return null;

            int pixelCount  = _width * _height;
            int dataBytes   = 3 * pixelCount * sizeof(float);
            byte[] blob = new byte[ExposureAccumulationBlobFormat.HeaderSize + dataBytes];

            int pos = ExposureAccumulationBlobFormat.HeaderSize;
            ExposureAccumulationBlobFormat.WriteHeader(blob, _width, _height, 3, _frameCount, ExposureAccumulationBlobFormat.CpuBackend);

            System.Buffer.BlockCopy(_sumR, 0, blob, pos, pixelCount * sizeof(float)); pos += pixelCount * sizeof(float);
            System.Buffer.BlockCopy(_sumG, 0, blob, pos, pixelCount * sizeof(float)); pos += pixelCount * sizeof(float);
            System.Buffer.BlockCopy(_sumB, 0, blob, pos, pixelCount * sizeof(float));
            return blob;
        }

        public bool DeserializeAccumulation(byte[] data, out int frameCount)
        {
            frameCount = 0;
            if (!ExposureAccumulationBlobFormat.TryReadHeader(data, out ExposureAccumulationBlobHeader header)) return false;
            if (header.Width != _width || header.Height != _height || header.ChannelCount != 3 || header.BackendTag != ExposureAccumulationBlobFormat.CpuBackend) return false;

            int pixelCount = header.Width * header.Height;
            int expected   = ExposureAccumulationBlobFormat.GetTotalByteCount(header.Width, header.Height, header.ChannelCount);
            if (data.Length < expected) return false;

            int pos = ExposureAccumulationBlobFormat.HeaderSize;
            System.Buffer.BlockCopy(data, pos, _sumR, 0, pixelCount * sizeof(float)); pos += pixelCount * sizeof(float);
            System.Buffer.BlockCopy(data, pos, _sumG, 0, pixelCount * sizeof(float)); pos += pixelCount * sizeof(float);
            System.Buffer.BlockCopy(data, pos, _sumB, 0, pixelCount * sizeof(float));
            _frameCount = header.FrameCount;
            frameCount  = header.FrameCount;
            return true;
        }

        public void Dispose() { } // No unmanaged resources; satisfies IDisposable via IExposureAccumulator.
    }
}
