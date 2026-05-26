using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Phototesting.CameraCapture.Exposure
{
    /// <summary>Typed header extracted from a raw accumulation blob by <see cref="ExposureAccumulationBlobFormat.TryReadHeader"/>.</summary>
    internal readonly record struct ExposureAccumulationBlobHeader(int Width, int Height, int ChannelCount, int FrameCount, int BackendTag);

    /// <summary>
    /// Binary layout for persisted exposure accumulation blobs.
    /// A blob begins with a 28-byte header (magic, version, dimensions, frame count) followed
    /// by the raw per-channel float sums. Used by <see cref="IExposureAccumulator.SerializeAccumulation"/>
    /// and read back by <see cref="PartialExposureSealer"/>.
    /// </summary>
    internal static class ExposureAccumulationBlobFormat
    {
        internal const int Magic = unchecked((int)0x50455853); // "PEXS"
        internal const int Version = 2;
        internal const int HeaderSize = sizeof(int) * 7;

        /// <summary>Backend tag written into blobs produced by the CPU accumulator (<see cref="ExposureAccumulationBuffer"/>).</summary>
        internal const int CpuBackend = 0;
        /// <summary>Backend tag written into blobs produced by the GPU accumulator (<see cref="GpuExposureAccumulator"/>).</summary>
        internal const int GpuBackend = 1;

        /// <summary>Returns the total byte size of a blob for the given buffer dimensions and channel count, including the header.</summary>
        internal static int GetTotalByteCount(int width, int height, int channelCount)
        {
            return checked(HeaderSize + checked(width * height * channelCount * sizeof(float)));
        }

        /// <summary>Writes the 28-byte blob header into <paramref name="blob"/> starting at offset 0.</summary>
        internal static void WriteHeader(byte[] blob, int width, int height, int channelCount, int frameCount, int backendTag)
        {
            WriteInt(blob, 0, Magic);
            WriteInt(blob, 4, Version);
            WriteInt(blob, 8, width);
            WriteInt(blob, 12, height);
            WriteInt(blob, 16, channelCount);
            WriteInt(blob, 20, frameCount);
            WriteInt(blob, 24, backendTag);
        }

        /// <summary>Attempts to parse the 28-byte header from <paramref name="data"/>. Returns <see langword="false"/> on a magic/version mismatch, invalid dimensions, or truncated data.</summary>
        internal static bool TryReadHeader(byte[] data, out ExposureAccumulationBlobHeader header)
        {
            header = default;
            if (data.Length < HeaderSize) return false;

            int magic = ReadInt(data, 0);
            int version = ReadInt(data, 4);
            int width = ReadInt(data, 8);
            int height = ReadInt(data, 12);
            int channelCount = ReadInt(data, 16);
            int frameCount = ReadInt(data, 20);
            int backendTag = ReadInt(data, 24);

            if (magic != Magic || version != Version) return false;
            if (width <= 0 || height <= 0 || channelCount <= 0 || frameCount < 0) return false;

            header = new ExposureAccumulationBlobHeader(width, height, channelCount, frameCount, backendTag);
            return true;
        }

        private static void WriteInt(byte[] blob, int offset, int value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(offset, sizeof(int)), value);
        }

        private static int ReadInt(byte[] blob, int offset)
        {
            return BinaryPrimitives.ReadInt32LittleEndian(blob.AsSpan(offset, sizeof(int)));
        }

        /// <summary>
        /// Converts a 4-channel (RGBA) GPU blob into a 3-channel (RGB) CPU blob by discarding the alpha channel.
        /// Returns <see langword="false"/> when the blob is not a valid 4-channel GPU blob or is truncated.
        /// </summary>
        internal static bool TryConvertGpuBlobToCpuBlob(byte[] data, ExposureAccumulationBlobHeader header, out byte[] cpuBlob)
        {
            cpuBlob = Array.Empty<byte>();
            if (header.ChannelCount != 4) return false;

            int expectedByteCount = GetTotalByteCount(header.Width, header.Height, header.ChannelCount);
            if (data.Length < expectedByteCount) return false;

            int pixelCount = checked(header.Width * header.Height);
            ReadOnlySpan<byte> gpuPayloadBytes = data.AsSpan(HeaderSize, pixelCount * 4 * sizeof(float));
            ReadOnlySpan<float> gpuPayload = MemoryMarshal.Cast<byte, float>(gpuPayloadBytes);

            cpuBlob = new byte[GetTotalByteCount(header.Width, header.Height, 3)];
            WriteHeader(cpuBlob, header.Width, header.Height, 3, header.FrameCount, CpuBackend);

            Span<float> cpuPayload = MemoryMarshal.Cast<byte, float>(cpuBlob.AsSpan(HeaderSize));
            Span<float> cpuR = cpuPayload.Slice(0, pixelCount);
            Span<float> cpuG = cpuPayload.Slice(pixelCount, pixelCount);
            Span<float> cpuB = cpuPayload.Slice(pixelCount * 2, pixelCount);

            for (int pixelIndex = 0; pixelIndex < pixelCount; pixelIndex++)
            {
                int rgbaOffset = pixelIndex * 4;
                cpuR[pixelIndex] = gpuPayload[rgbaOffset + 0];
                cpuG[pixelIndex] = gpuPayload[rgbaOffset + 1];
                cpuB[pixelIndex] = gpuPayload[rgbaOffset + 2];
            }

            return true;
        }
    }
}