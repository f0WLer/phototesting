using OpenTK.Graphics.OpenGL;
using SkiaSharp;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Phototesting.AdminTooling;
using Phototesting.CameraCapture.Rendering;
using Phototesting.ImageEffects;
using Phototesting.PhotoSync.Storage;

namespace Phototesting.CameraCapture.Exposure
{
    internal enum ExposureState { Idle, Capturing, Paused, Done }

    // Persistent renderer that drives a VirtualCamera across N consecutive game frames,
    // accumulating pixel data into an ExposureAccumulationBuffer via naive averaging.
    //
    // Lifecycle:
    //   Start() → Capturing → (Pause/Resume) → Done (when target frames reached)
    //   Export() produces a PNG at any point once frames have been accumulated.
    //   Reset() clears the buffer and returns to Capturing from the same position.
    //   Stop() tears down the camera and returns to Idle.
    //
    // Registered at EnumRenderStage.Before (RenderOrder 0.4) so VirtualCamera.RenderCamera
    // can call TriggerRenderStage(Opaque) safely. Controlled entirely via admin commands.
    internal sealed class VirtualExposureRenderer : IRenderer, IDisposable
    {
        private readonly ICoreClientAPI _capi;
        private readonly ClientPlatformWindows _platform;
        private readonly ClientMain _main;
        private readonly WetplateEffectsConfig _baselineEffects;

        private VirtualCamera? _camera;
        private ExposureAccumulationBuffer? _buffer;
        private int _targetFrameCount;
        private int _previewFrameInterval;
        private bool _disposed;

        // When set, the exposure renderer pushes developed preview frames here while capturing,
        // keeping the debug preview window live during long exposures.
        internal IExposurePreviewSink? PreviewSink { get; set; }

        internal ExposureState State { get; private set; } = ExposureState.Idle;
        internal int FramesAccumulated => _buffer?.FramesAccumulated ?? 0;
        internal int TargetFrameCount => _targetFrameCount;

        // Physics layer toggles — persisted across Start()/Reset() and applied to each new buffer.
        internal bool PhysicsLinearize      = true;
        internal bool PhysicsSpectralWeights = true;
        internal bool PhysicsHDCurve         = true;

        // Copies the current physics settings onto a buffer.
        private void ApplyPhysicsToBuffer(ExposureAccumulationBuffer buf)
        {
            buf.LinearizeInput      = PhysicsLinearize;
            buf.ApplySpectralWeights = PhysicsSpectralWeights;
            buf.ApplyHDCurve         = PhysicsHDCurve;
        }

        // Updates a named physics flag on both the renderer and the live buffer (if any).
        // Returns false if name is unrecognised.
        internal bool SetPhysics(string flag, bool value)
        {
            switch (flag)
            {
                case "linearize":   PhysicsLinearize      = value; break;
                case "spectral":    PhysicsSpectralWeights = value; break;
                case "hdcurve":     PhysicsHDCurve         = value; break;
                default: return false;
            }
            if (_buffer != null) ApplyPhysicsToBuffer(_buffer);
            return true;
        }

        public double RenderOrder => 0.4;
        public int RenderRange => 0;

        internal VirtualExposureRenderer(ICoreClientAPI capi)
        {
            _capi = capi;
            _main = (ClientMain)capi.World;
            _platform = (ClientPlatformWindows)_main.Platform;
            _baselineEffects = ImageEffectsPipelineBridge.LoadCaptureBaseline(capi);
        }

        // Starts a new exposure from the given world position and heading.
        // Replaces any currently in-progress exposure and resets accumulated data.
        internal void Start(Vec3d eyePos, float yaw, float pitch, float fov, int frameCount)
        {
            StopCamera();
            _targetFrameCount = Math.Max(1, frameCount);

            VirtualCamera cam = new VirtualCamera(_capi, _platform, _main);
            cam.CameraPos = eyePos.Clone();
            cam.Yaw = yaw;
            cam.Pitch = pitch;
            cam.Fov = fov;
            cam.Dimension = _capi.World.Player.Entity.Pos.Dimension;
            cam.InitBuffer();
            _camera = cam;

            _buffer = new ExposureAccumulationBuffer(_capi.Render.FrameWidth, _capi.Render.FrameHeight, _targetFrameCount);
            ApplyPhysicsToBuffer(_buffer);
            // Update preview roughly 8 times across the exposure regardless of frame count.
            _previewFrameInterval = Math.Max(1, _targetFrameCount / 8);
            PreviewSink?.BeginExposurePassthrough();
            State = ExposureState.Capturing;
        }

        // Pauses frame accumulation. Only valid in Capturing state.
        internal void Pause()
        {
            if (State == ExposureState.Capturing)
                State = ExposureState.Paused;
        }

        // Resumes frame accumulation. Only valid in Paused state.
        internal void Resume()
        {
            if (State == ExposureState.Paused)
                State = ExposureState.Capturing;
        }

        // Stops the exposure session, destroys the camera, and returns to Idle.
        internal void Stop()
        {
            StopCamera();
            _buffer = null;
            PreviewSink?.EndExposurePassthrough();
            State = ExposureState.Idle;
        }

        // Clears accumulated frames and resumes capturing from the same camera position.
        // No-op when Idle or when no camera is alive.
        internal void Reset()
        {
            if (_buffer == null || _camera == null) return;
            _buffer.Reset();
            State = ExposureState.Capturing;
        }

        // Develops the current accumulated buffer through the wetplate effects pipeline and saves a PNG.
        // Can be called in any non-Idle state that has at least one accumulated frame.
        // Does not change state so the caller can continue accumulating or export repeatedly.
        // Throws InvalidOperationException when no frames are available.
        // Throws on file I/O or effects failure; caller is responsible for catching.
        internal string Export(WetplateEffectsConfig? effectsOverride = null)
        {
            if (_buffer == null || _buffer.FramesAccumulated == 0)
                throw new InvalidOperationException("No frames accumulated.");

            using SKBitmap averaged = _buffer.Develop();

            int maxDimension = PhotoTestingConfigAccess.ResolveClientConfig(_capi)?.Viewfinder?.PhotoCaptureMaxDimension
                ?? ViewfinderConfig.DefaultPhotoCaptureMaxDimension;

            float scale = Math.Min(1f, maxDimension / (float)Math.Max(averaged.Width, averaged.Height));
            int outW = Math.Max(1, (int)(averaged.Width * scale));
            int outH = Math.Max(1, (int)(averaged.Height * scale));

            SKBitmap working;
            if (scale < 0.9999f)
            {
                var dstInfo = new SKImageInfo(outW, outH, SKColorType.Bgra8888, SKAlphaType.Opaque);
                working = new SKBitmap(dstInfo);
                using (var canvas = new SKCanvas(working))
                {
                    canvas.Clear(SKColors.Black);
                    canvas.DrawBitmap(averaged, new SKRect(0, 0, outW, outH));
                }
            }
            else
            {
                working = averaged.Copy();
            }

            SKBitmap cropped = PhotoCaptureRenderer.CenterCropToPlateAspect(working);
            if (!ReferenceEquals(cropped, working)) working.Dispose();

            try
            {
                WetplateEffectsConfig profile = ImageEffectsPipelineBridge.ResolveCaptureProfile(_baselineEffects, effectsOverride);
                ImageEffectsPipelineBridge.ApplyCaptureEffects(cropped, "exposure-export", profile);

                string now = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string rnd = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
                string fileName = $"exposure_{now}_{rnd}.png";
                string fullPath = PhotoAssetStoragePaths.GetPhotoPath(fileName);

                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                using var finalImage = SKImage.FromBitmap(cropped);
                using var pngData = finalImage.Encode(SKEncodedImageFormat.Png, 90);
                using var output = File.Open(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
                pngData.SaveTo(output);

                return fileName;
            }
            finally
            {
                cropped.Dispose();
            }
        }

        // Develops and shapes one debug-preview frame using the same crop/scale/effects policy
        // as the normal virtual camera preview path.
        private void PushPreviewFrame()
        {
            if (_buffer == null || PreviewSink == null || _buffer.FramesAccumulated == 0) return;

            ViewfinderConfig? cfg = PhotoTestingConfigAccess.ResolveClientConfig(_capi)?.Viewfinder;
            int maxDimension = cfg?.DebugPreviewMaxDimension ?? ViewfinderConfig.DefaultPhotoCaptureMaxDimension;

            using SKBitmap developed = _buffer.Develop();

            float scale = Math.Min(1f, maxDimension / (float)Math.Max(developed.Width, developed.Height));
            int outW = Math.Max(1, (int)(developed.Width * scale));
            int outH = Math.Max(1, (int)(developed.Height * scale));

            var dstInfo = new SKImageInfo(outW, outH, SKColorType.Bgra8888, SKAlphaType.Opaque);
            using SKBitmap scaled = new SKBitmap(dstInfo);
            using (var canvas = new SKCanvas(scaled))
            {
                canvas.Clear(SKColors.Black);
                canvas.DrawBitmap(developed, new SKRect(0, 0, outW, outH));
            }

            SKBitmap cropped = PhotoCaptureRenderer.CenterCropToPlateAspect(scaled);
            try
            {
                if (cfg?.DebugPreviewApplyEffects ?? true)
                {
                    WetplateEffectsConfig profile = ImageEffectsPipelineBridge.ResolveCaptureProfile(_baselineEffects, null);
                    ImageEffectsPipelineBridge.ApplyCaptureEffects(cropped, "exposure-preview", profile);
                }

                PreviewSink.StoreExposureFrame(cropped);
            }
            finally
            {
                if (!ReferenceEquals(cropped, scaled)) cropped.Dispose();
            }
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (State != ExposureState.Capturing) return;
            if (_camera == null || _buffer == null) return;

            // Reinitialize FBO and reset the buffer if the window was resized between frames.
            // Mixed-dimension frames cannot be averaged so accumulated data must be discarded.
            if (_capi.Render.FrameWidth != _camera.fbo.Width || _capi.Render.FrameHeight != _camera.fbo.Height)
            {
                _camera.Destroy();
                _camera.InitBuffer();
                _buffer.Reset();
                _buffer = new ExposureAccumulationBuffer(_capi.Render.FrameWidth, _capi.Render.FrameHeight, _targetFrameCount);
                ApplyPhysicsToBuffer(_buffer);
                _capi.Logger.Warning("Phototesting: window resized during exposure — accumulated frames discarded.");
            }

            try
            {
                int savedDimension = _capi.World.Player.Entity.Pos.Dimension;
                _capi.World.Player.Entity.Pos.Dimension = _camera.Dimension;
                _camera.RenderCamera(deltaTime);
                _capi.World.Player.Entity.Pos.Dimension = savedDimension;

                // Clear the primary framebuffer that RenderCamera may have left in an intermediate state.
                GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

                using SKBitmap raw = VirtualCaptureService.ReadFramebuffer(_capi, _camera.fbo);
                _buffer.Accumulate(raw);

                // Push a preview update on the first frame and then every _previewFrameInterval frames.
                if (PreviewSink != null)
                {
                    int n = _buffer.FramesAccumulated;
                    if (n == 1 || n % _previewFrameInterval == 0)
                    {
                        PushPreviewFrame();
                    }
                }
            }
            catch (Exception ex)
            {
                _capi.Logger.Warning($"Phototesting: exposure frame {_buffer.FramesAccumulated} render failed: {ex.Message}");
                State = ExposureState.Paused;
                return;
            }

            if (_buffer.FramesAccumulated >= _targetFrameCount)
            {
                State = ExposureState.Done;
                // Push the final fully-developed frame so the preview shows the complete exposure.
                PushPreviewFrame();
                _capi.Logger.Notification(
                    $"Phototesting: exposure complete — {_buffer.FramesAccumulated}/{_targetFrameCount} frames accumulated. " +
                    "Use '.phototesting exposure export' to save.");
            }
        }

        private void StopCamera()
        {
            if (_camera == null) return;
            BestEffort.Try(null, "destroy virtual exposure camera", () => _camera.Destroy());
            _camera = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopCamera();
            _buffer = null;
            PreviewSink?.EndExposurePassthrough();
        }
    }
}
