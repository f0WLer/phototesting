using SkiaSharp;
using Phototesting.CameraCapture;
using Phototesting.CameraCapture.Rendering;
using Phototesting.ImageEffects;
using Phototesting.PhotoSync.Storage;

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
        /// Loads the <c>.pex</c> for <paramref name="exposureId"/>, renders it with the given chemistry profile,
        /// target-frame normalization, output size, and effects settings, deletes the file on success, and
        /// returns the saved PNG file name.
        /// Returns <see langword="null"/> when no partial exists, the blob is corrupt, or rendering fails.
        /// </summary>
        internal static string? SealToPng(
            string exposureId,
            PlateProcessProfile profile,
            int targetFrameCount,
            int maxDimension,
            WetplateEffectsConfig baselineEffects,
            WetplateEffectsConfig? effectsOverride = null)
        {
            if (!ExposureAccumulationStore.TryLoad(exposureId, out byte[]? data)) return null;

            string? fileName = RenderBlobToPng(data, profile, targetFrameCount, maxDimension, baselineEffects, effectsOverride);
            if (!string.IsNullOrEmpty(fileName))
            {
                ExposureAccumulationStore.Delete(exposureId);
            }

            return fileName;
        }

        private static string? RenderBlobToPng(
            byte[] data,
            PlateProcessProfile profile,
            int targetFrameCount,
            int maxDimension,
            WetplateEffectsConfig baselineEffects,
            WetplateEffectsConfig? effectsOverride)
        {
            if (!ExposureAccumulationBlobFormat.TryReadHeader(data, out var header)) return null;
            if (header.FrameCount <= 0) return null;

            byte[] cpuCompatibleData;
            switch (header.BackendTag)
            {
                case ExposureAccumulationBlobFormat.CpuBackend:
                    if (header.ChannelCount != 3) return null;
                    cpuCompatibleData = data;
                    break;
                case ExposureAccumulationBlobFormat.GpuBackend:
                    if (!ExposureAccumulationBlobFormat.TryConvertGpuBlobToCpuBlob(data, header, out cpuCompatibleData)) return null;
                    break;
                default:
                    return null;
            }

            var buffer = new ExposureAccumulationBuffer(header.Width, header.Height, Math.Max(1, targetFrameCount));
            buffer.RedSensitivity        = profile.RedSensitivity;
            buffer.GreenSensitivity      = profile.GreenSensitivity;
            buffer.BlueSensitivity       = profile.BlueSensitivity;
            buffer.DevelopmentStrength   = profile.DevelopmentStrength;
            buffer.HDGamma               = profile.HDGamma;

            if (!buffer.DeserializeAccumulation(cpuCompatibleData, out _)) return null;

            using SKBitmap developed = buffer.Develop();
            SKBitmap cropped = PhotoCropMath.ScaleDownAndCenterCropToPlateAspect(developed, maxDimension);
            try
            {
                WetplateEffectsConfig effects = ImageEffectsPipelineBridge.ResolveCaptureProfile(baselineEffects, effectsOverride);
                ImageEffectsPipelineBridge.ApplyCaptureEffects(cropped, "plate-tray-development", effects);
                return PhotoAssetStoragePaths.SaveExposurePng(cropped);
            }
            finally
            {
                cropped.Dispose();
            }
        }

    }
}
