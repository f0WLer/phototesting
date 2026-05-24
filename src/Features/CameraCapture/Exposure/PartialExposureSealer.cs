using SkiaSharp;
using Phototesting.CameraCapture;
using Phototesting.CameraCapture.Rendering;
using Phototesting.ImageEffects;
using Phototesting.PhotoSync.Storage;
using System.Runtime.InteropServices;

namespace Phototesting.CameraCapture.Exposure
{
    /// <summary>
    /// Develops a saved partial accumulation blob (<c>.pex</c> file) into a finalised PNG on disk.
    /// Called when an <c>ExposurePaused</c> plate is committed to a development tray rather than
    /// being resumed in the camera. The <c>.pex</c> file is deleted only after a successful render so
    /// incompatible or corrupt partials are not destroyed during a failed tray-seal attempt.
    /// Finishing effects are applied here — accumulator paths must have <c>ApplyFinishing = false</c>.
    /// </summary>
    internal static class PartialExposureSealer
    {
        /// <summary>
        /// Loads the <c>.pex</c> for <paramref name="exposureId"/>, renders it with the given chemistry profile
        /// and optional effects override, deletes the file on success, and returns the saved PNG file name.
        /// Returns <see langword="null"/> when no partial exists, the blob is corrupt, or rendering fails.
        /// </summary>
        internal static string? SealToPng(string exposureId, PlateProcessProfile profile, WetplateEffectsConfig? effectsOverride = null)
        {
            if (string.IsNullOrEmpty(exposureId)) return null;
            if (!ExposureAccumulationStore.TryLoad(exposureId, out byte[]? data) || data == null) return null;

            string? fileName = RenderBlobToPng(data, profile, effectsOverride);
            if (!string.IsNullOrEmpty(fileName))
            {
                ExposureAccumulationStore.Delete(exposureId);
            }

            return fileName;
        }

        private static string? RenderBlobToPng(byte[] data, PlateProcessProfile profile, WetplateEffectsConfig? effectsOverride)
        {
            if (!ExposureAccumulationBlobFormat.TryReadHeader(data, out var header)) return null;
            if (header.FrameCount <= 0) return null;
            if (!TryGetCpuCompatibleBlob(data, header, out byte[] cpuCompatibleData)) return null;

            var buffer = new ExposureAccumulationBuffer(header.Width, header.Height, header.FrameCount);
            buffer.LinearizeInput       = true;
            buffer.ApplySpectralWeights = true;
            buffer.ApplyHDCurve         = true;
            buffer.RedSensitivity        = profile.RedSensitivity;
            buffer.GreenSensitivity      = profile.GreenSensitivity;
            buffer.BlueSensitivity       = profile.BlueSensitivity;
            buffer.DevelopmentStrength   = profile.DevelopmentStrength;
            buffer.HDGamma               = profile.HDGamma;

            if (!buffer.DeserializeAccumulation(cpuCompatibleData, out _)) return null;

            using SKBitmap developed = buffer.Develop();
            SKBitmap cropped = PhotoCropMath.ScaleDownAndCenterCropToPlateAspect(developed, ViewfinderConfig.DefaultPhotoCaptureMaxDimension);
            try
            {
                WetplateEffectsConfig effects = ImageEffectsPipelineBridge.ResolveCaptureProfile(new WetplateEffectsConfig(), effectsOverride);
                ImageEffectsPipelineBridge.ApplyCaptureEffects(cropped, "plate-tray-development", effects);
                return PhotoAssetStoragePaths.SaveExposurePng(cropped);
            }
            finally
            {
                cropped.Dispose();
            }
        }

        private static bool TryGetCpuCompatibleBlob(byte[] data, ExposureAccumulationBlobHeader header, out byte[] cpuCompatibleData)
        {
            cpuCompatibleData = Array.Empty<byte>();

            switch (header.BackendTag)
            {
                case ExposureAccumulationBlobFormat.CpuBackend:
                    cpuCompatibleData = data;
                    return header.ChannelCount == 3;

                case ExposureAccumulationBlobFormat.GpuBackend:
                    return TryConvertGpuBlobToCpuBlob(data, header, out cpuCompatibleData);

                default:
                    return false;
            }
        }

        private static bool TryConvertGpuBlobToCpuBlob(byte[] data, ExposureAccumulationBlobHeader header, out byte[] cpuBlob)
        {
            cpuBlob = Array.Empty<byte>();
            if (header.ChannelCount != 4) return false;

            int expectedByteCount = ExposureAccumulationBlobFormat.GetTotalByteCount(header.Width, header.Height, header.ChannelCount);
            if (data.Length < expectedByteCount) return false;

            int pixelCount = checked(header.Width * header.Height);
            ReadOnlySpan<byte> gpuPayloadBytes = data.AsSpan(ExposureAccumulationBlobFormat.HeaderSize, pixelCount * 4 * sizeof(float));
            ReadOnlySpan<float> gpuPayload = MemoryMarshal.Cast<byte, float>(gpuPayloadBytes);

            cpuBlob = new byte[ExposureAccumulationBlobFormat.GetTotalByteCount(header.Width, header.Height, 3)];
            ExposureAccumulationBlobFormat.WriteHeader(cpuBlob, header.Width, header.Height, 3, header.FrameCount, ExposureAccumulationBlobFormat.CpuBackend);

            Span<float> cpuPayload = MemoryMarshal.Cast<byte, float>(cpuBlob.AsSpan(ExposureAccumulationBlobFormat.HeaderSize));
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
