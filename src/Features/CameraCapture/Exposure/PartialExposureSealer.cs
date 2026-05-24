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
    /// being resumed in the camera. The <c>.pex</c> file is always deleted after the call regardless
    /// of rendering success, because the plate is being irrevocably committed to the development workflow.
    /// Finishing effects are applied here — accumulator paths must have <c>ApplyFinishing = false</c>.
    /// </summary>
    internal static class PartialExposureSealer
    {
        /// <summary>
        /// Loads the <c>.pex</c> for <paramref name="exposureId"/>, renders it with the given chemistry profile
        /// and optional effects override, deletes the file, and returns the saved PNG file name.
        /// Returns <see langword="null"/> when no partial exists, the blob is corrupt, or rendering fails.
        /// </summary>
        internal static string? SealToPng(string exposureId, PlateProcessProfile profile, WetplateEffectsConfig? effectsOverride = null)
        {
            if (string.IsNullOrEmpty(exposureId)) return null;
            if (!ExposureAccumulationStore.TryLoad(exposureId, out byte[]? data) || data == null) return null;

            string? fileName = null;
            try
            {
                fileName = RenderBlobToPng(data, profile, effectsOverride);
            }
            finally
            {
                ExposureAccumulationStore.Delete(exposureId);
            }

            return fileName;
        }

        private static string? RenderBlobToPng(byte[] data, PlateProcessProfile profile, WetplateEffectsConfig? effectsOverride)
        {
            if (!ExposureAccumulationBlobFormat.TryReadHeader(data, out var header)) return null;
            if (header.FrameCount <= 0) return null;

            var buffer = new ExposureAccumulationBuffer(header.Width, header.Height, header.FrameCount);
            buffer.LinearizeInput       = true;
            buffer.ApplySpectralWeights = true;
            buffer.ApplyHDCurve         = true;
            buffer.RedSensitivity        = profile.RedSensitivity;
            buffer.GreenSensitivity      = profile.GreenSensitivity;
            buffer.BlueSensitivity       = profile.BlueSensitivity;
            buffer.DevelopmentStrength   = profile.DevelopmentStrength;
            buffer.HDGamma               = profile.HDGamma;

            if (!buffer.DeserializeAccumulation(data, out _)) return null;

            using SKBitmap developed = buffer.Develop();
            SKBitmap cropped = PhotoCaptureRenderer.ScaleDownAndCenterCropToPlateAspect(developed, ViewfinderConfig.DefaultPhotoCaptureMaxDimension);
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
    }
}
