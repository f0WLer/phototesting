using System.Buffers;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace Phototesting.CameraCapture.Exposure
{
    // HDR floating-point accumulation buffer for multi-frame virtual camera exposure.
    // Accumulates per-channel linear sums from rendered frames.
    // Develop() divides by referenceFrameCount (the target exposure length), not the actual
    // frame count, so a shorter exposure is darker and a longer exposure is brighter,
    // with highlight clipping as the sums exceed the reference level.
    internal sealed class ExposureAccumulationBuffer
    {
        private readonly int _width;
        private readonly int _height;
        private readonly float[] _sumB;
        private readonly float[] _sumG;
        private readonly float[] _sumR;
        private int _frameCount;

        // The denominator used in Develop(). Fixed at construction time so output brightness
        // scales with accumulated frames relative to the intended exposure length.
        private readonly int _referenceFrameCount;

        internal int FramesAccumulated => _frameCount;
        internal int Width => _width;
        internal int Height => _height;

        internal ExposureAccumulationBuffer(int width, int height, int referenceFrameCount)
        {
            _width = width;
            _height = height;
            _referenceFrameCount = Math.Max(1, referenceFrameCount);
            int count = width * height;
            _sumB = new float[count];
            _sumG = new float[count];
            _sumR = new float[count];
        }

        // Resets the buffer to zero accumulated frames.
        internal void Reset()
        {
            Array.Clear(_sumB, 0, _sumB.Length);
            Array.Clear(_sumG, 0, _sumG.Length);
            Array.Clear(_sumR, 0, _sumR.Length);
            _frameCount = 0;
        }

        // Accumulates one BGRA8888 frame into the running channel sums.
        // Frames with dimensions other than Width × Height are ignored.
        internal void Accumulate(SKBitmap frame)
        {
            if (frame.Width != _width || frame.Height != _height) return;

            int pixelCount = _width * _height;
            int byteCount = pixelCount * 4;
            byte[] bytes = ArrayPool<byte>.Shared.Rent(byteCount);
            try
            {
                Marshal.Copy(frame.GetPixels(), bytes, 0, byteCount);

                for (int i = 0; i < pixelCount; i++)
                {
                    int b = i * 4;
                    _sumB[i] += bytes[b + 0] / 255f;
                    _sumG[i] += bytes[b + 1] / 255f;
                    _sumR[i] += bytes[b + 2] / 255f;
                    // alpha (b+3) is ignored — virtual camera FBO output is always fully opaque
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(bytes);
            }

            _frameCount++;
        }

        // Develops accumulated frames into a new BGRA8888 SKBitmap.
        // Output brightness is scaled against referenceFrameCount, not actual frame count:
        //   frames < reference  → underexposed (dark)
        //   frames = reference  → normally exposed
        //   frames > reference  → overexposed (bright, highlights clip to white)
        // Returns a black image when no frames have been accumulated.
        // Caller owns and is responsible for disposing the returned bitmap.
        internal SKBitmap Develop()
        {
            var info = new SKImageInfo(_width, _height, SKColorType.Bgra8888, SKAlphaType.Opaque);
            var bitmap = new SKBitmap(info);

            int pixelCount = _width * _height;
            int byteCount = pixelCount * 4;
            byte[] bytes = ArrayPool<byte>.Shared.Rent(byteCount);
            try
            {
                float invFrames = 1f / _referenceFrameCount;

                for (int i = 0; i < pixelCount; i++)
                {
                    int b = i * 4;
                    bytes[b + 0] = ToByte(_sumB[i] * invFrames);
                    bytes[b + 1] = ToByte(_sumG[i] * invFrames);
                    bytes[b + 2] = ToByte(_sumR[i] * invFrames);
                    bytes[b + 3] = 255;
                }

                Marshal.Copy(bytes, 0, bitmap.GetPixels(), byteCount);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(bytes);
            }

            return bitmap;
        }

        private static byte ToByte(float v)
        {
            if (v <= 0f) return 0;
            if (v >= 1f) return 255;
            return (byte)(v * 255f + 0.5f);
        }
    }
}
