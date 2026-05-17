using OpenTK.Graphics.OpenGL;
using SkiaSharp;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;
using Phototesting.AdminTooling;
using Phototesting.CameraCapture.Rendering;
using Phototesting.ImageEffects;
using Phototesting.PhotoSync.Storage;

namespace Phototesting.CameraCapture.Exposure
{
    internal enum ExposureState { Idle, Capturing, Paused, Done }

    // Persistent renderer that drives a VirtualCamera across consecutive game frames,
    // accumulating pixel data into an ExposureAccumulationBuffer.
    //
    // Lifecycle:
    //   Start() -> Capturing -> (Pause/Resume) -> Done (when stopped or cap reached)
    //   Export() produces a PNG at any point once frames have been accumulated.
    //   Reset() clears the buffer and returns to Capturing from the same position.
    //   Stop() tears down the camera and preserves the accumulated buffer for export.
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
        private PlateProcessProfile _process = PlateProcessProfile.Iodide;
        private float _elapsedSinceLastSample;
        private float _elapsedSinceLastPreview;

        // Wall-clock shutter timing (milliseconds from _capi.ElapsedMilliseconds).
        private long _shutterStartMs;
        private long _shutterEndMs;
        private long _pauseStartedMs;

        private bool _disposed;

        // When set, the exposure renderer pushes developed preview frames here while capturing,
        // keeping the debug preview window live during long exposures.
        internal IExposurePreviewSink? PreviewSink { get; set; }

        // Process profile applied to the current or next exposure session.
        // Controls timing (duration, sample count) and emulsion response (spectral weights, H&D curve).
        internal PlateProcessProfile ActiveProcess => _process;

        internal ExposureState State { get; private set; } = ExposureState.Idle;
        internal int FramesAccumulated => _buffer?.FramesAccumulated ?? 0;
        internal int CapFrameCount => _process.SampleCount;

        // Wall-clock time elapsed since shutter open / remaining until close.
        // Returns 0 when no exposure is active.
        internal float ElapsedSeconds
            => _shutterStartMs == 0 ? 0f : Math.Max(0f, (_capi.ElapsedMilliseconds - _shutterStartMs) / 1000f);
        internal float RemainingSeconds
            => _shutterEndMs == 0 ? 0f : Math.Max(0f, (_shutterEndMs - _capi.ElapsedMilliseconds) / 1000f);

        // Physics layer toggles; persisted across Start()/Reset() and applied to each new buffer.
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

        internal void Start(VirtualCameraState cameraState, PlateProcessProfile process)
        {
            Discard();
            _process = process;
            _elapsedSinceLastSample  = 0f;
            _elapsedSinceLastPreview = 0f;

            long now = _capi.ElapsedMilliseconds;
            _shutterStartMs = now;
            _shutterEndMs   = now + (long)(process.DurationSeconds * 1000f);
            _pauseStartedMs = 0;

            VirtualCamera cam = new VirtualCamera(_capi, _platform, _main);
            cam.ApplyState(cameraState);
            cam.InitBuffer();
            _camera = cam;

            AllocateBufferForCurrentFrameSize();
            PreviewSink?.BeginExposurePassthrough();
            State = ExposureState.Capturing;
        }

        // Pauses frame accumulation. Only valid in Capturing state.
        internal void Pause()
        {
            if (State == ExposureState.Capturing)
            {
                _pauseStartedMs = _capi.ElapsedMilliseconds;
                State = ExposureState.Paused;
            }
        }

        // Resumes frame accumulation. Only valid in Paused state.
        internal void Resume()
        {
            if (State == ExposureState.Paused)
            {
                // Extend the shutter window by however long we were paused.
                long pausedFor = _capi.ElapsedMilliseconds - _pauseStartedMs;
                _shutterStartMs += pausedFor;
                _shutterEndMs   += pausedFor;
                State = ExposureState.Capturing;
            }
        }

        // Closes the shutter, stops the camera, and leaves the buffer ready for export.
        // The accumulated image is preserved. Call Discard() to also clear the buffer.
        internal void Stop()
        {
            StopCamera();
            State = ExposureState.Done;
        }

        // Destroys the accumulated buffer and returns to Idle. Use after export or to abandon a session.
        internal void Discard()
        {
            StopCamera();
            _buffer = null;
            _shutterStartMs = 0;
            _shutterEndMs   = 0;
            PreviewSink?.EndExposurePassthrough();
            State = ExposureState.Idle;
        }

        // Clears accumulated frames and resumes capturing from the same camera position.
        // No-op when Idle or when no camera is alive.
        internal void Reset()
        {
            if (_buffer == null || _camera == null) return;
            _buffer.Reset();
            _elapsedSinceLastSample  = 0f;
            _elapsedSinceLastPreview = 0f;
            long now = _capi.ElapsedMilliseconds;
            _shutterStartMs = now;
            _shutterEndMs   = now + (long)(_process.DurationSeconds * 1000f);
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

            SKBitmap cropped = PhotoCaptureRenderer.ScaleDownAndCenterCropToPlateAspect(averaged, maxDimension);

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

            SKBitmap cropped = PhotoCaptureRenderer.ScaleDownAndCenterCropToPlateAspect(developed, maxDimension);
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
                cropped.Dispose();
            }
        }

        // Minimum wall-clock seconds between consecutive preview pushes.
        private const float PreviewCadenceSeconds = 0.25f;

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (State != ExposureState.Capturing) return;
            if (_camera == null || _buffer == null) return;

            // Always advance both timers so preview cadence and sample interval track real time
            // independently of each other and of the game's frame rate.
            _elapsedSinceLastSample  += deltaTime;
            _elapsedSinceLastPreview += deltaTime;

            // Reinitialize FBO and reset the buffer if the window was resized between frames.
            // Mixed-dimension frames cannot be averaged so accumulated data must be discarded.
            if (_capi.Render.FrameWidth != _camera.fbo.Width || _capi.Render.FrameHeight != _camera.fbo.Height)
            {
                ReinitializeCameraAndBufferForResize();
                _capi.Logger.Warning("Phototesting: window resized during exposure — accumulated frames discarded.");
            }

            // Wall-clock shutter close: shutter has been open long enough.
            long nowMs = _capi.ElapsedMilliseconds;
            if (nowMs >= _shutterEndMs)
            {
                State = ExposureState.Done;
                PushPreviewFrame();
                _capi.Logger.Notification(
                    $"Phototesting: {_process.Name} exposure complete — " +
                    $"{_buffer.FramesAccumulated} samples over {(nowMs - _shutterStartMs) / 1000f:F2}s. " +
                    $"Use '.phototesting exposure export' to save.");
                return;
            }

            // Rate limiter: never sample faster than the process cadence.
            if (_elapsedSinceLastSample < _process.SampleInterval) return;
            _elapsedSinceLastSample -= _process.SampleInterval;

            try
            {
                _camera.RenderCameraInStoredDimension(deltaTime);

                // Clear the primary framebuffer that RenderCamera may have left in an intermediate state.
                GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

                using SKBitmap raw = VirtualCaptureService.ReadFramebuffer(_capi, _camera.fbo);
                _buffer.Accumulate(raw);
            }
            catch (Exception ex)
            {
                _capi.Logger.Warning($"Phototesting: exposure frame {_buffer.FramesAccumulated} render failed: {ex.Message}");
                State = ExposureState.Paused;
                return;
            }

            // Push preview on wall-clock cadence; only after new data has been accumulated
            // so the preview reflects the latest exposure state without redundant develop calls.
            if (PreviewSink != null && _buffer.FramesAccumulated > 0 &&
                (_buffer.FramesAccumulated == 1 || _elapsedSinceLastPreview >= PreviewCadenceSeconds))
            {
                _elapsedSinceLastPreview = 0f;
                PushPreviewFrame();
            }
        }

        private void StopCamera()
        {
            if (_camera == null) return;
            BestEffort.Try(null, "destroy virtual exposure camera", () => _camera.Destroy());
            _camera = null;
        }

        private void AllocateBufferForCurrentFrameSize()
        {
            _buffer = new ExposureAccumulationBuffer(_capi.Render.FrameWidth, _capi.Render.FrameHeight, _process.SampleCount);
            ApplyPhysicsToBuffer(_buffer);
            _buffer.RedSensitivity      = _process.RedSensitivity;
            _buffer.GreenSensitivity    = _process.GreenSensitivity;
            _buffer.BlueSensitivity     = _process.BlueSensitivity;
            _buffer.DevelopmentStrength = _process.DevelopmentStrength;
            _buffer.HDGamma             = _process.HDGamma;
        }

        private void ReinitializeCameraAndBufferForResize()
        {
            if (_camera == null) return;

            _camera.Destroy();
            _camera.InitBuffer();
            AllocateBufferForCurrentFrameSize();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Discard();
        }
    }
}
