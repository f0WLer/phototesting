using OpenTK.Graphics.OpenGL;
using SkiaSharp;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Phototesting.CameraCapture.Exposure;
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
                _capi.World.Player.Entity.SidedPos.Dimension));

            cam.InitBuffer();

            OneShotRenderer renderer = null!;
            renderer = new OneShotRenderer(dt =>
            {
                try
                {
                    cam.RenderCameraInStoredDimension(dt);
                    GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

                    using SKBitmap raw = ReadFramebuffer(_capi, cam.fbo);
                    using SKBitmap croppedBitmap = PhotoCaptureRenderer.ScaleDownAndCenterCropToPlateAspect(raw, maxDimension);

                    EmulsionDevelop.ApplyInPlace(croppedBitmap, PlateProcessProfile.Iodide);

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

        // Reads pixels from a virtual FBO into a SkiaSharp bitmap through the engine screenshot path.
        internal static SKBitmap ReadFramebuffer(ICoreClientAPI capi, FrameBufferRef framebuffer)
        {
            return ClientFramebufferCapture.ReadToSkBitmap(capi, framebuffer);
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

            internal OneShotRenderer(Action<float> onRender)
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
