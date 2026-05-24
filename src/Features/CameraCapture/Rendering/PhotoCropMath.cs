using SkiaSharp;
using Vintagestory.API.MathTools;

namespace Phototesting.CameraCapture.Rendering
{
    internal static class PhotoCropMath
    {
        // Computes the centered UV keep-fraction needed to crop a source image down to a target aspect ratio.
        public static void ComputeCenterCrop(float sourceAspect, float targetAspect, out float keepU, out float keepV, float keepBias = 1f)
        {
            keepU = 1f;
            keepV = 1f;

            if (sourceAspect <= 0f || targetAspect <= 0f)
            {
                return;
            }

            if (sourceAspect > targetAspect)
            {
                keepU = GameMath.Clamp((targetAspect / sourceAspect) * keepBias, 0f, 1f);
            }
            else
            {
                keepV = GameMath.Clamp((sourceAspect / targetAspect) * keepBias, 0f, 1f);
            }
        }

        // Center-crops a bitmap to the 10:11 plate aspect ratio.
        internal static SKBitmap CenterCropToPlateAspect(SKBitmap source)
        {
            if (source.Width <= 0 || source.Height <= 0) return source;

            const float plateTargetAspect = 10f / 11f;
            float sourceAspect = source.Width / (float)source.Height;

            ComputeCenterCrop(sourceAspect, plateTargetAspect, out float keepU, out float keepV);

            if (keepU >= 0.9999f && keepV >= 0.9999f)
            {
                return source;
            }

            int cropW = Math.Max(1, (int)Math.Round(source.Width * keepU));
            int cropH = Math.Max(1, (int)Math.Round(source.Height * keepV));

            int cropX = Math.Max(0, (source.Width - cropW) / 2);
            int cropY = Math.Max(0, (source.Height - cropH) / 2);

            if (cropX + cropW > source.Width) cropW = source.Width - cropX;
            if (cropY + cropH > source.Height) cropH = source.Height - cropY;

            var dstInfo = new SKImageInfo(cropW, cropH, SKColorType.Bgra8888, SKAlphaType.Opaque);
            var cropped = new SKBitmap(dstInfo);

            using (var canvas = new SKCanvas(cropped))
            {
                canvas.Clear(SKColors.Black);
                canvas.DrawBitmap(
                    source,
                    new SKRectI(cropX, cropY, cropX + cropW, cropY + cropH),
                    new SKRect(0, 0, cropW, cropH));
            }

            return cropped;
        }

        // Scales a bitmap down to fit within maxDimension, then center-crops to plate aspect.
        internal static SKBitmap ScaleDownAndCenterCropToPlateAspect(SKBitmap source, int maxDimension)
        {
            float scale = Math.Min(1f, maxDimension / (float)Math.Max(source.Width, source.Height));
            int outW = Math.Max(1, (int)(source.Width * scale));
            int outH = Math.Max(1, (int)(source.Height * scale));

            var dstInfo = new SKImageInfo(outW, outH, SKColorType.Bgra8888, SKAlphaType.Opaque);
            SKBitmap scaled = new SKBitmap(dstInfo);
            using (var canvas = new SKCanvas(scaled))
            {
                canvas.Clear(SKColors.Black);
                canvas.DrawBitmap(source, new SKRect(0, 0, outW, outH));
            }

            SKBitmap cropped = CenterCropToPlateAspect(scaled);
            if (!ReferenceEquals(cropped, scaled))
            {
                scaled.Dispose();
            }

            return cropped;
        }
    }
}
