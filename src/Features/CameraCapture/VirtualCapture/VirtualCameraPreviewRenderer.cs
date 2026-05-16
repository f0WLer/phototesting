using OpenTK.Graphics.OpenGL;
using SkiaSharp;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Phototesting.AdminTooling;
using Phototesting.CameraCapture.Exposure;
using Phototesting.CameraCapture.Rendering;
using Phototesting.ImageEffects;

namespace Phototesting.CameraCapture
{
    // Persistent virtual camera renderer for the debug preview overlay.
    // Holds a fixed-position off-screen VirtualCamera and re-renders it on a timed interval,
    // storing processed frames in a ViewfinderPreviewFrameBuffer for ViewfinderDebugPreviewRenderer to display.
    // Registered at EnumRenderStage.Before so it can call TriggerRenderStage(Opaque) safely.
    internal sealed class VirtualCameraPreviewRenderer : IRenderer, IDisposable, IExposurePreviewSink
    {
        private readonly ICoreClientAPI _capi;
        private readonly ClientPlatformWindows _platform;
        private readonly ClientMain _main;
        private readonly WetplateEffectsConfig _baselineEffects;
        private readonly ViewfinderPreviewFrameBuffer _previewBuffer = new();

        private VirtualCamera? _camera;
        private int _maxDimension;
        private WetplateEffectsConfig? _effectsOverride;
        private long _lastRenderMs;
        private bool _disposed;

        public double RenderOrder => 0.4;
        public int RenderRange => 0;

        // True when a virtual camera is currently active, or while the exposure renderer is feeding frames.
        internal bool IsActive => _camera != null || _exposurePassthrough;

        // When true this renderer skips its own camera rendering and accepts frames pushed by VirtualExposureRenderer.
        private bool _exposurePassthrough;

        // Exposure preview takes ownership of this renderer's output surface and releases any live camera.
        public void BeginExposurePassthrough()
        {
            StopCamera();
            _exposurePassthrough = true;
        }

        // Re-enables normal mode and clears the last pushed exposure frame.
        public void EndExposurePassthrough()
        {
            _exposurePassthrough = false;
            _previewBuffer.Clear();
        }

        // Stores an already-developed exposure bitmap into the preview buffer.
        // Ignored unless BeginExposurePassthrough() was called.
        public void StoreExposureFrame(SKBitmap bmp)
        {
            if (_exposurePassthrough)
                _previewBuffer.StoreFrame(bmp);
        }

        internal VirtualCameraPreviewRenderer(ICoreClientAPI capi)
        {
            _capi = capi;
            _main = (ClientMain)capi.World;
            _platform = (ClientPlatformWindows)_main.Platform;
            _baselineEffects = ImageEffectsPipelineBridge.LoadCaptureBaseline(capi);
        }

        // Starts the virtual camera preview at the given world position and heading.
        // Destroys and replaces any previously active camera.
        internal void Start(Vec3d eyePos, float yaw, float pitch, float fov, int maxDimension, WetplateEffectsConfig? effectsOverride = null)
        {
            EndExposurePassthrough();
            StopCamera();
            _maxDimension = maxDimension;
            _effectsOverride = effectsOverride;

            VirtualCamera cam = new VirtualCamera(_capi, _platform, _main);
            cam.CameraPos = eyePos.Clone();
            cam.Yaw = yaw;
            cam.Pitch = pitch;
            cam.Fov = fov;
            cam.Dimension = _capi.World.Player.Entity.Pos.Dimension;
            cam.InitBuffer();

            _camera = cam;
            _lastRenderMs = 0; // Trigger render on the very next eligible tick.
        }

        // Stops the virtual camera preview and clears buffered frames.
        internal void Stop()
        {
            EndExposurePassthrough();
            StopCamera();
        }

        private void StopCamera()
        {
            if (_camera == null) return;
            BestEffort.Try(null, "destroy virtual camera preview", () => _camera.Destroy());
            _camera = null;
        }

        // Forwards to the internal preview frame buffer. Returns true when a new frame is available.
        internal bool TryConsumeLatestFrame(out int[] bgraPixels, out int width, out int height)
            => _previewBuffer.TryConsumeLatestFrame(out bgraPixels, out width, out height);

        // Re-renders the virtual scene and stores the result when the refresh interval has elapsed.
        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            // Exposure renderer is the frame source — nothing for us to render.
            if (_exposurePassthrough) return;
            if (_camera == null) return;

            ViewfinderConfig? cfg = PhotoTestingConfigAccess.ResolveClientConfig(_capi)?.Viewfinder;
            int refreshMs = cfg?.DebugPreviewRefreshMs ?? 500;
            long nowMs = _capi.ElapsedMilliseconds;
            if (nowMs - _lastRenderMs < refreshMs) return;
            _lastRenderMs = nowMs;

            // Reinitialize the FBO if the window was resized since last render.
            if (_capi.Render.FrameWidth != _camera.fbo.Width || _capi.Render.FrameHeight != _camera.fbo.Height)
            {
                _camera.Destroy();
                _camera.InitBuffer();
            }

            try
            {
                int savedDimension = _capi.World.Player.Entity.Pos.Dimension;
                _capi.World.Player.Entity.Pos.Dimension = _camera.Dimension;
                _camera.RenderCamera(deltaTime);
                _capi.World.Player.Entity.Pos.Dimension = savedDimension;

                // Clear the primary FBO that RenderCamera may have left in an intermediate state.
                GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

                using SKBitmap raw = VirtualCaptureService.ReadFramebuffer(_capi, _camera.fbo);

                float scale = Math.Min(1f, _maxDimension / (float)Math.Max(raw.Width, raw.Height));
                int outW = Math.Max(1, (int)(raw.Width * scale));
                int outH = Math.Max(1, (int)(raw.Height * scale));

                var dstInfo = new SKImageInfo(outW, outH, SKColorType.Bgra8888, SKAlphaType.Opaque);
                SKBitmap scaledBitmap = new SKBitmap(dstInfo);
                using (var canvas = new SKCanvas(scaledBitmap))
                {
                    canvas.Clear(SKColors.Black);
                    canvas.DrawBitmap(raw, new SKRect(0, 0, outW, outH));
                }

                SKBitmap croppedBitmap = PhotoCaptureRenderer.CenterCropToPlateAspect(scaledBitmap);
                if (!ReferenceEquals(croppedBitmap, scaledBitmap)) scaledBitmap.Dispose();

                if (cfg?.DebugPreviewApplyEffects ?? true)
                {
                    WetplateEffectsConfig profile = ImageEffectsPipelineBridge.ResolveCaptureProfile(_baselineEffects, _effectsOverride);
                    ImageEffectsPipelineBridge.ApplyCaptureEffects(croppedBitmap, "virtualpreview", profile);
                }

                _previewBuffer.StoreFrame(croppedBitmap);
                croppedBitmap.Dispose();
            }
            catch (Exception ex)
            {
                _capi.Logger.Warning($"Phototesting: virtual camera preview render failed: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
