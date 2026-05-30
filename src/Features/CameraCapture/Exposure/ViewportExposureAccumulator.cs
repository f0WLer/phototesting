using SkiaSharp;
using Vintagestory.API.Client;
using Phototesting.AdminTooling;
using Phototesting.CameraCapture.Rendering;
using Phototesting.ImageEffects;
using Phototesting.PhotoSync.Storage;

namespace Phototesting.CameraCapture.Exposure
{
    /// <summary>
    /// Accumulates frames from the player's live viewport into an <see cref="ExposureAccumulationBuffer"/>.
    /// Registered as an <c>IRenderer</c> at <c>EnumRenderStage.AfterBlit</c> while actively capturing;
    /// blits the back buffer into a downsampled FBO and reads that smaller frame via <c>GL.ReadPixels</c>
    /// each sample interval before feeding the bytes to the CPU accumulator.
    /// <para>Lifecycle: <see cref="Start"/> → <see cref="ExposureState.Capturing"/> → optional
    /// <see cref="Pause"/>/<see cref="Resume"/> → <see cref="Stop"/> or <see cref="Export"/>.
    /// <see cref="OnAutoHalt"/> fires when a timer or sample-count stop policy is satisfied.</para>
    /// </summary>
    internal sealed class ViewportExposureAccumulator : IGameplayExposureAccumulator, IRenderer
    {
        private readonly ICoreClientAPI _capi;
        private readonly WetplateEffectsConfig _baselineEffects;
        private ExposureAccumulationBuffer? _buffer;
        private ExposureReadbackPipeline? _readback;
        private byte[]? _readbackScratch;
        private int _primingKicksRemaining;
        private PlateProcessProfile _process;
        private ExposureStartOptions _startOptions;
        private float _elapsedSinceLastSample;
        private float _elapsedCaptureSeconds;
        private bool _rendererRegistered;
        private bool _disposed;

        /// <summary>Fired when an auto-halt policy transitions the accumulator from <see cref="ExposureState.Capturing"/> to <see cref="ExposureState.Done"/>.</summary>
        internal Action? OnAutoHalt { get; set; }

        public ExposureState State { get; private set; } = ExposureState.Idle;
        public bool IsCapturing => State == ExposureState.Capturing;
        public int FramesAccumulated => _buffer?.FramesAccumulated ?? 0;
        public int TargetFrames => _process.SampleCount;

        public double RenderOrder => 0.3;
        public int RenderRange => 0;

        internal ViewportExposureAccumulator(ICoreClientAPI capi)
        {
            _capi = capi;
            _baselineEffects = ImageEffectsPipelineBridge.LoadCaptureBaseline(capi);
        }

        /// <summary>
        /// Allocates the async PBO readback pipeline and registers this renderer at <c>AfterBlit</c>
        /// so the 3-slot ring is already warm by the time the player opens the shutter.
        /// Call at viewfinder entry (before the shutter is ever pressed) so the ring completes
        /// its <see cref="ExposureReadbackPipeline.RingSize"/> priming kicks during normal aiming;
        /// the first <see cref="Start"/> tick then maps a real frame immediately with no sync stall
        /// and no dropped sample from the priming gap.
        /// </summary>
        internal void Prime()
        {
            if (_disposed) return;
            int w = Math.Max(1, _capi.Render.FrameWidth);
            int h = Math.Max(1, _capi.Render.FrameHeight);
            EnsureReadbackResources(w, h);
            RegisterRenderer();
            _primingKicksRemaining = ExposureReadbackPipeline.RingSize;
        }

        /// <summary>
        /// Starts a fresh accumulation session with the given chemistry and stop policy.
        /// If already <see cref="ExposureState.Paused"/>, resumes instead. No-op when already capturing.
        /// </summary>
        internal void Start(PlateProcessProfile process, ExposureStartOptions startOptions)
        {
            if (_disposed) return;
            if (State == ExposureState.Paused) { Resume(); return; }
            if (State == ExposureState.Capturing) return;

            _process = process;
            _startOptions = startOptions;
            _elapsedSinceLastSample = 0f;
            _elapsedCaptureSeconds = 0f;

            int w = Math.Max(1, _capi.Render.FrameWidth);
            int h = Math.Max(1, _capi.Render.FrameHeight);
            EnsureReadbackResources(w, h);
            _buffer?.Dispose();
            _buffer = new ExposureAccumulationBuffer(_readback!.Width, _readback.Height, process.SampleCount);
            ApplyProcessToBuffer(_buffer, process);

            // Stop any in-flight priming kicks; the first OnRenderFrame tick transitions
            // directly to the accumulation branch.
            _primingKicksRemaining = 0;

            // Register renderer only if Prime() hasn't already done so during viewfinder aiming.
            if (!_rendererRegistered) RegisterRenderer();
            State = ExposureState.Capturing;
            ViewportExposureSuppressContext.ExposureCapturing = true;
        }

        public void Pause()
        {
            if (State == ExposureState.Capturing)
            {
                State = ExposureState.Paused;
                ViewportExposureSuppressContext.ExposureCapturing = false;
            }
        }

        public void Resume()
        {
            if (State == ExposureState.Paused)
            {
                State = ExposureState.Capturing;
                ViewportExposureSuppressContext.ExposureCapturing = true;
            }
        }

        public void Stop()
        {
            // Done: renderer unregistration was already deferred via EnqueueMainThreadTask in the
            // auto-halt path; calling UnregisterRenderer() here from within the render loop would
            // crash the game. Idle: nothing to do.
            if (State == ExposureState.Idle || State == ExposureState.Done) return;
            ViewportExposureSuppressContext.ExposureCapturing = false;
            UnregisterRenderer();
            State = ExposureState.Done;
        }

        /// <summary>
        /// Develops the buffer, applies wetplate finishing effects, and saves a PNG.
        /// Returns the saved file name. Throws when no frames have been accumulated.
        /// </summary>
        public string Export(WetplateEffectsConfig? effectsOverride = null)
        {
            if (_buffer == null || _buffer.FramesAccumulated == 0)
                throw new InvalidOperationException("ViewportExposureAccumulator: no frames accumulated.");

            int maxDim = PhotoTestingConfigAccess.ResolveClientConfig(_capi)?.Viewfinder?.PhotoCaptureMaxDimension
                ?? ViewfinderConfig.DefaultPhotoCaptureMaxDimension;

            using SKBitmap developed = _buffer.Develop();
            SKBitmap cropped = PhotoCropMath.ScaleDownAndCenterCropToPlateAspect(developed, maxDim);

            try
            {
                WetplateEffectsConfig profile = ImageEffectsPipelineBridge.ResolveCaptureProfile(_baselineEffects, effectsOverride);
                ImageEffectsPipelineBridge.ApplyCaptureEffects(cropped, "viewport-exposure", profile);

                return PhotoAssetStoragePaths.SaveExposurePng(cropped);
            }
            finally
            {
                cropped.Dispose();
            }
        }

        /// <summary>
        /// Serializes the current accumulated frame sums for pause/resume and tray-seal workflows.
        /// Returns <see langword="null"/> when no frames have been accumulated.
        /// </summary>
        internal byte[]? ExportPartial()
        {
            return _buffer?.SerializeAccumulation();
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            // Priming branch: issue blit+async-ReadPixels into the PBO ring without accumulating.
            // Runs during viewfinder aiming (before shutter press) so the ring is already warm
            // when Start() transitions to Capturing.  Any outputs mapped during priming are
            // discarded — the PBO ring will have been fully cycled by the time real samples arrive.
            if (_primingKicksRemaining > 0)
            {
                int pw = _capi.Render.FrameWidth, ph = _capi.Render.FrameHeight;
                EnsureReadbackResources(pw, ph);
                _readback!.SubmitFrameAndCollectReadback(0, pw, ph, _readbackScratch!);
                _primingKicksRemaining--;
                return;
            }

            if (State != ExposureState.Capturing || _buffer == null) return;

            _elapsedCaptureSeconds += deltaTime;
            if (_startOptions.StopMode == ExposureStartOptions.ExposureStopMode.Timer && _elapsedCaptureSeconds >= _startOptions.StopAfterSeconds)
            {
                CompleteAutoStop();
                return;
            }

            _elapsedSinceLastSample += deltaTime;
            if (_elapsedSinceLastSample < _process.SampleInterval) return;
            _elapsedSinceLastSample -= _process.SampleInterval;

            int w = _capi.Render.FrameWidth;
            int h = _capi.Render.FrameHeight;

            int previousBufferWidth = _buffer.Width;
            int previousBufferHeight = _buffer.Height;
            EnsureReadbackResources(w, h);

            // Reset buffer on viewport resize or readback-size policy change to prevent averaging mixed-dimension frames.
            if (_buffer.Width != _readback!.Width || _buffer.Height != _readback.Height)
            {
                _buffer.Dispose();
                _buffer = new ExposureAccumulationBuffer(_readback.Width, _readback.Height, _process.SampleCount);
                ApplyProcessToBuffer(_buffer, _process);

                if (previousBufferWidth != _readback.Width || previousBufferHeight != _readback.Height)
                    _capi.Logger.Warning("Phototesting: viewport readback size changed during exposure — accumulated frames discarded.");
            }

            // Async PBO path: blit back buffer (ID 0) → downsample FBO, issue ReadPixels into write PBO,
            // and map the PBO written 2 kicks ago (guaranteed ready — no CPU stall).
            if (_readback.SubmitFrameAndCollectReadback(0, w, h, _readbackScratch!))
            {
                byte[] pixels = _readbackScratch!;
                // Ensure fully opaque alpha so the accumulator treats every pixel as solid.
                for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
                _buffer.Accumulate(pixels, _readback.Width, _readback.Height);
            }

            // Auto-halt once the target sample count is reached.
            // Do NOT call Stop() here — UnregisterRenderer() must not be called from within
            // a renderer iteration loop (causes ArgumentOutOfRangeException). Instead, set
            // Done immediately to stop accumulating and defer the unregister to the next frame.
            if (_startOptions.StopMode == ExposureStartOptions.ExposureStopMode.TargetSamples && _buffer.FramesAccumulated >= _process.SampleCount)
                CompleteAutoStop();
        }

        private void CompleteAutoStop()
        {
            ViewportExposureSuppressContext.ExposureCapturing = false;
            // Drain PBOs still in-flight (up to RingSize-1 samples) before sealing so the
            // last few frames aren't silently dropped at shutter close.
            if (_readback != null && _readbackScratch != null && _buffer != null)
            {
                _readback.DrainPending(_readbackScratch, pixels =>
                {
                    for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
                    _buffer.Accumulate(pixels, _readback.Width, _readback.Height);
                });
            }
            State = ExposureState.Done;
            // Clear the flag NOW (before OnAutoHalt fires) so any Dispose() call triggered
            // by the auto-stop callback chain (e.g. Registry.Remove → Dispose) hits the
            // early-return guard in UnregisterRenderer() instead of crashing the iterator.
            // The deferred task calls the API directly because the flag is already false.
            _rendererRegistered = false;
            _capi.Event.EnqueueMainThreadTask(
                () => _capi.Event.UnregisterRenderer(this, EnumRenderStage.AfterBlit),
                "phototesting-unregister-exposure");
            OnAutoHalt?.Invoke();
        }

        private static void ApplyProcessToBuffer(ExposureAccumulationBuffer buf, PlateProcessProfile process)
        {
            buf.RedSensitivity = process.RedSensitivity;
            buf.GreenSensitivity = process.GreenSensitivity;
            buf.BlueSensitivity = process.BlueSensitivity;
            buf.DevelopmentStrength = process.DevelopmentStrength;
            buf.HDGamma = process.HDGamma;
        }

        private void EnsureReadbackResources(int sourceWidth, int sourceHeight)
        {
            int maxDimension = PhotoTestingConfigAccess.ResolveClientConfig(_capi)?.Viewfinder?.ExposureReadbackMaxDimension
                ?? ViewfinderConfig.DefaultExposureReadbackMaxDimension;

            _readback ??= new ExposureReadbackPipeline(_capi);
            bool resized = _readback.EnsureAllocated(sourceWidth, sourceHeight, maxDimension);
            if (resized || _readbackScratch == null)
                _readbackScratch = new byte[_readback.Width * _readback.Height * 4];
        }

        private void RegisterRenderer()
        {
            if (_rendererRegistered) return;
            _capi.Event.RegisterRenderer(this, EnumRenderStage.AfterBlit, "phototesting-viewport-exposure");
            _rendererRegistered = true;
        }

        private void UnregisterRenderer()
        {
            if (!_rendererRegistered) return;
            _capi.Event.UnregisterRenderer(this, EnumRenderStage.AfterBlit);
            _rendererRegistered = false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ViewportExposureSuppressContext.ExposureCapturing = false;
            UnregisterRenderer();
            _buffer?.Dispose();
            _buffer = null;
            _readback?.Dispose();
            _readback = null;
            _readbackScratch = null;
            State = ExposureState.Idle;
        }
    }
}
