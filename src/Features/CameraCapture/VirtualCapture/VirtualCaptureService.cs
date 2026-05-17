using OpenTK.Graphics.OpenGL;
using SkiaSharp;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Phototesting.CameraCapture.Rendering;
using Phototesting.ImageEffects;
using Phototesting.PhotoSync.Storage;

namespace Phototesting.CameraCapture
{
    // One-shot virtual camera capture service.
    // Encapsulates FBO allocation, scene rendering, framebuffer readback, effects, and PNG save.
    // Does not register a persistent renderer; each capture registers and removes its own one-shot IRenderer.
    internal sealed class VirtualCaptureService : IDisposable
    {
        private readonly ICoreClientAPI _capi;
        private readonly ClientPlatformWindows _platform;
        private readonly ClientMain _main;
        private readonly WetplateEffectsConfig _baselineEffects;

        private bool _capturing;
        private bool _disposed;

        internal bool IsCapturing => _capturing;

        internal VirtualCaptureService(ICoreClientAPI capi)
        {
            _capi = capi;
            _main = (ClientMain)capi.World;
            _platform = (ClientPlatformWindows)_main.Platform;
            _baselineEffects = ImageEffectsPipelineBridge.LoadCaptureBaseline(capi);
        }

        // Renders one virtual frame from the given eye position, reads it back, applies the wetplate
        // effects pipeline, and saves a PNG. Fires onSuccess(fileName) or onError on completion.
        // Does nothing when already capturing.
        internal void TryCaptureOneShot(
            Vec3d eyePos,
            float yaw,
            float pitch,
            float fov,
            int maxDimension,
            WetplateEffectsConfig? effectsOverride,
            Action<string> onSuccess,
            Action<Exception> onError)
        {
            if (_capturing) return;
            _capturing = true;

            string now = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string rnd = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
            string fileName = $"wetplate_{now}_{rnd}.png";
            string fullPath = PhotoAssetStoragePaths.GetPhotoPath(fileName);

            VirtualCamera cam = new VirtualCamera(_capi, _platform, _main);
            cam.ApplyState(new VirtualCameraState(
                eyePos,
                yaw,
                pitch,
                fov,
                _capi.World.Player.Entity.Pos.Dimension));

            cam.InitBuffer();

            OneShotRenderer renderer = null!;
            renderer = new OneShotRenderer(_capi.Render.FrameWidth, _capi.Render.FrameHeight, dt =>
            {
                try
                {
                    cam.RenderCameraInStoredDimension(dt);
                    GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

                    using SKBitmap raw = ReadFramebuffer(_capi, cam.fbo);
                    using SKBitmap croppedBitmap = PhotoCaptureRenderer.ScaleDownAndCenterCropToPlateAspect(raw, maxDimension);

                    WetplateEffectsConfig profile = ImageEffectsPipelineBridge.ResolveCaptureProfile(_baselineEffects, effectsOverride);
                    ImageEffectsPipelineBridge.ApplyCaptureEffects(croppedBitmap, fileName, profile);

                    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                    using var finalImage = SKImage.FromBitmap(croppedBitmap);
                    using var pngData = finalImage.Encode(SKEncodedImageFormat.Png, 90);
                    using var output = File.Open(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    pngData.SaveTo(output);

                    _capi.Event.UnregisterRenderer(renderer, EnumRenderStage.Before);
                    cam.Destroy();
                    _capturing = false;

                    onSuccess(fileName);
                }
                catch (Exception ex)
                {
                    try { _capi.Event.UnregisterRenderer(renderer, EnumRenderStage.Before); } catch { }
                    try { cam.Destroy(); } catch { }
                    _capturing = false;
                    onError(ex);
                }
            });

            _capi.Event.RegisterRenderer(renderer, EnumRenderStage.Before, "phototesting-virtualcapture");
        }

        // Reads pixels from a virtual FBO into a SkiaSharp bitmap.
        // Applies 180-degree rotation and horizontal mirror to correct for OpenGL's bottom-left origin.
        internal static SKBitmap ReadFramebuffer(ICoreClientAPI capi, FrameBufferRef framebuffer)
        {
            SKBitmap bmp = new SKBitmap(framebuffer.Width, framebuffer.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, framebuffer.FboId);
            GL.ReadPixels(0, 0, framebuffer.Width, framebuffer.Height, PixelFormat.Bgra, PixelType.UnsignedByte, bmp.GetPixels());

            SKBitmap corrected = new SKBitmap(bmp.Width, bmp.Height, bmp.ColorType, SKAlphaType.Opaque);
            using SKCanvas canvas = new SKCanvas(corrected);
            canvas.Translate(bmp.Width, bmp.Height);
            canvas.RotateDegrees(180f);
            canvas.Scale(-1f, 1f, (float)bmp.Width / 2f, 0f);
            canvas.DrawBitmap(bmp, 0f, 0f);
            bmp.Dispose();

            return corrected;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            // Capturing state is cleaned up by the in-flight renderer's own error path.
        }

        // Single-use renderer that fires once at EnumRenderStage.Before and invokes an action.
        private sealed class OneShotRenderer : IRenderer
        {
            private readonly Action<float> _onRender;

            public double RenderOrder => 0.5;
            public int RenderRange => 0;

            internal OneShotRenderer(int width, int height, Action<float> onRender)
            {
                _onRender = onRender;
            }

            public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
            {
                _onRender(deltaTime);
            }

            public void Dispose() { }
        }
    }
}
