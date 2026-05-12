using System.Buffers;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL4;
using SkiaSharp;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Phototesting.AdminTooling;
using Phototesting.ImageEffects;

namespace Phototesting.CameraCapture.Rendering
{
    // Framebuffer capture, wetplate post-processing, and png write pipeline.
    // Also serves optional debug preview frame generation for tuning.
    public class PhotoCaptureRenderer : IRenderer
    {
        private readonly ICoreClientAPI _capi;
        private int _captureMaxDimension = ViewfinderConfig.DefaultPhotoCaptureMaxDimension;

        private WetplateEffectsConfig _effectsConfig = new();

        private PendingCapture? _pending;
        private readonly object _pendingLock = new object();
        private readonly ViewfinderPreviewFrameBuffer _previewFrameBuffer = new ViewfinderPreviewFrameBuffer();

        private class PendingCapture
        {
            public readonly string FileName;
            public readonly string FullPath;
            public readonly Action<string> OnSuccess;
            public readonly Action<Exception> OnError;
            /// <summary>When set, overrides the renderer's global effectsConfig for this capture only.</summary>
            public readonly WetplateEffectsConfig? EffectsOverride;

            // Captures immutable per-request metadata for a queued screenshot job.
            public PendingCapture(string fileName, string fullPath, Action<string> onSuccess, Action<Exception> onError, WetplateEffectsConfig? effectsOverride = null)
            {
                FileName = fileName;
                FullPath = fullPath;
                OnSuccess = onSuccess;
                OnError = onError;
                EffectsOverride = effectsOverride;
            }
        }

        // Creates a capture renderer bound to the active client API and initial effects config.
        public PhotoCaptureRenderer(ICoreClientAPI capi)
        {
            this._capi = capi;
            _effectsConfig = ImageEffectsPipelineBridge.LoadCaptureBaseline(capi);
        }

        // Reloads the default effects profile used for subsequent captures and previews.
        public void ReloadEffectsConfig()
        {
            _effectsConfig = ImageEffectsPipelineBridge.LoadCaptureBaseline(_capi);
        }

        // Sets the maximum output dimension used when capture frames are downscaled before saving.
        public void SetCaptureMaxDimension(int maxDimension)
        {
            if (maxDimension < ViewfinderConfig.MinPhotoCaptureMaxDimension) maxDimension = ViewfinderConfig.MinPhotoCaptureMaxDimension;
            if (maxDimension > ViewfinderConfig.MaxPhotoCaptureMaxDimension) maxDimension = ViewfinderConfig.MaxPhotoCaptureMaxDimension;
            _captureMaxDimension = maxDimension;
        }

        // Queues a debug preview capture.
        public void RequestDebugPreviewFrame(int maxDimension, WetplateEffectsConfig? effectsOverride = null)
        {
            if (maxDimension < ViewfinderConfig.MinPhotoCaptureMaxDimension) maxDimension = ViewfinderConfig.MinPhotoCaptureMaxDimension;
            if (maxDimension > ViewfinderConfig.MaxPhotoCaptureMaxDimension) maxDimension = ViewfinderConfig.MaxPhotoCaptureMaxDimension;
            _previewFrameBuffer.RequestCapture(maxDimension, effectsOverride);
        }

        // Returns and clears the latest prepared debug preview frame if one is available.
        public bool TryConsumeDebugPreviewFrame(out int[] bgraPixels, out int width, out int height)
        {
            return _previewFrameBuffer.TryConsumeLatestFrame(out bgraPixels, out width, out height);
        }

        // Queues one capture for the next render frame.
        public bool TryScheduleCapture(out string fileName, Action<string> onSuccess, Action<Exception> onError, WetplateEffectsConfig? effectsOverride = null)
        {
            lock (_pendingLock)
            {
                if (_pending != null)
                {
                    fileName = string.Empty;
                    return false;
                }

                string now = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string rnd = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
                fileName = $"wetplate_{now}_{rnd}.png";

                string modDataPath = Path.Combine(GamePaths.DataPath, "ModData", "phototesting", "photos");
                string fullPath = Path.Combine(modDataPath, fileName);

                _pending = new PendingCapture(fileName, fullPath, onSuccess, onError, effectsOverride);
                return true;
            }
        }

        public double RenderOrder => 0;
        public int RenderRange => 0;

        // Processes one pending capture and one optional preview request.
        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            PendingCapture? toProcess;
            lock (_pendingLock)
            {
                toProcess = _pending;
                if (toProcess != null) _pending = null;
            }

            var previewRequest = _previewFrameBuffer.TakePendingCaptureRequest();

            if (toProcess != null)
            {
                try
                {
                    using SKBitmap dstBitmap = BuildProcessedCaptureBitmap(_captureMaxDimension, toProcess.FileName, toProcess.EffectsOverride);

                    using var finalImage = SKImage.FromBitmap(dstBitmap);
                    using var pngData = finalImage.Encode(SKEncodedImageFormat.Png, PngCompressionQuality);

                    Directory.CreateDirectory(Path.GetDirectoryName(toProcess.FullPath)!);
                    using (var output = File.Open(toProcess.FullPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        pngData.SaveTo(output);
                    }

                    toProcess.OnSuccess(toProcess.FileName);
                }
                catch (Exception ex)
                {
                    toProcess.OnError(ex);
                }
            }

            if (previewRequest.HasValue)
            {
                try
                {
                    int previewMaxDimension = previewRequest.Value.MaxDimension;
                    WetplateEffectsConfig? previewEffectsOverride = previewRequest.Value.EffectsOverride;
                    using SKBitmap previewBitmap = BuildProcessedCaptureBitmap(previewMaxDimension, "phototesting-debug-preview", previewEffectsOverride);
                    _previewFrameBuffer.StoreFrame(previewBitmap);
                }
                catch (Exception ex)
                {
                    _capi.Logger.Warning($"Phototesting: debug preview capture failed: {ex.Message}");
                }
            }
        }

        // Clears queued capture and preview state.
        public void Dispose()
        {
            lock (_pendingLock)
            {
                _pending = null;
            }

            _previewFrameBuffer.Clear();
        }

        private PhotoCapturePipelineConfig? PipelineCfg => PhotoTestingConfigAccess.ResolveClientConfig(_capi)?.PhotoCapturePipeline;

        // Gets the blank-frame sampling divisor.
        private int BlankDetectSampleDivisor => PipelineCfg?.BlankDetectSampleDivisor ?? 32;

        // Gets PNG compression quality.
        private int PngCompressionQuality => PipelineCfg?.PngCompressionQuality ?? 90;

        // Center-crops a capture to plate aspect.
        private static SKBitmap CenterCropToPlateAspect(SKBitmap source)
        {
            if (source.Width <= 0 || source.Height <= 0) return source;

            const float plateTargetAspect = 10f / 11f;
            float sourceAspect = source.Width / (float)source.Height;

            PhotoCropMath.ComputeCenterCrop(sourceAspect, plateTargetAspect, out float keepU, out float keepV);

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

        // Captures, flips, crops, and effects the framebuffer.
        private SKBitmap BuildProcessedCaptureBitmap(int maxDimension, string seedKey, WetplateEffectsConfig? effectsOverride = null)
        {
            int width = _capi.Render.FrameWidth;
            int height = _capi.Render.FrameHeight;
            int pixelByteCount = width * height * 4;

            byte[] pixels = ArrayPool<byte>.Shared.Rent(pixelByteCount);
            GL.PixelStore(PixelStoreParameter.PackAlignment, 1);

            SKBitmap? dstBitmap = null;
            try
            {
                ReadCapturePixels(width, height, pixelByteCount, pixels);

                for (int i = 3; i < pixelByteCount; i += 4)
                {
                    pixels[i] = 255;
                }

                var srcInfo = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
                using var srcBitmap = new SKBitmap(srcInfo);
                Marshal.Copy(pixels, 0, srcBitmap.GetPixels(), pixelByteCount);

                float scale = Math.Min(1f, maxDimension / (float)Math.Max(width, height));
                int outW = Math.Max(1, (int)(width * scale));
                int outH = Math.Max(1, (int)(height * scale));

                var dstInfo = new SKImageInfo(outW, outH, SKColorType.Bgra8888, SKAlphaType.Opaque);
                dstBitmap = new SKBitmap(dstInfo);
                using (var canvas = new SKCanvas(dstBitmap))
                {
                    canvas.Clear(SKColors.Black);
                    canvas.Scale(1, -1);
                    canvas.Translate(0, -outH);
                    using var srcImage = SKImage.FromBitmap(srcBitmap);
                    canvas.DrawImage(srcImage, new SKRect(0, 0, outW, outH));
                }

                SKBitmap croppedBitmap = CenterCropToPlateAspect(dstBitmap);
                if (!ReferenceEquals(croppedBitmap, dstBitmap))
                {
                    dstBitmap.Dispose();
                    dstBitmap = croppedBitmap;
                }

                try
                {
                    WetplateEffectsConfig activeCfg = ImageEffectsPipelineBridge.ResolveCaptureProfile(_effectsConfig, effectsOverride);
                    ImageEffectsPipelineBridge.ApplyCaptureEffects(dstBitmap, seedKey, activeCfg);
                }
                catch (Exception effectEx)
                {
                    _capi.Logger.Error($"PhotoCapture: Effects failed: {effectEx.Message}");
                }

                return dstBitmap;
            }
            catch
            {
                dstBitmap?.Dispose();
                throw;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(pixels);
            }
        }

        // Reads from the default framebuffer, then retries from front buffer when the first read appears blank.
        private void ReadCapturePixels(int width, int height, int pixelByteCount, byte[] pixels)
        {
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
            GL.ReadBuffer(ReadBufferMode.Back);
            GL.ReadPixels(0, 0, width, height, PixelFormat.Bgra, PixelType.UnsignedByte, pixels);

            if (!LooksBlank(pixels, pixelByteCount)) return;

            GL.ReadBuffer(ReadBufferMode.Front);
            GL.ReadPixels(0, 0, width, height, PixelFormat.Bgra, PixelType.UnsignedByte, pixels);
        }

        // Detects obviously blank framebuffer reads so capture can retry against the front buffer when needed.
        private bool LooksBlank(byte[] pixels, int validByteCount)
        {
            // Heuristic: sample a few points; if all channels are 0, we likely read the wrong buffer.
            if (pixels == null || validByteCount < 16 || pixels.Length < validByteCount) return true;

            int step = Math.Max(4, validByteCount / BlankDetectSampleDivisor);
            // Align to full BGRA pixels so channel indexing never straddles byte boundaries.
            if ((step & 3) != 0)
            {
                step += 4 - (step & 3);
            }

            for (int i = 0; i <= validByteCount - 4; i += step)
            {
                // Check BGRA
                if (pixels[i] != 0 || pixels[i + 1] != 0 || pixels[i + 2] != 0)
                {
                    return false;
                }
            }
            return true;
        }
    }
}

