using System.Buffers;
using OpenTK.Graphics.OpenGL4;
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
    /// reads back the back buffer via <c>GL.ReadPixels</c> each sample interval and feeds the bytes
    /// to the CPU accumulator.
    /// <para>Lifecycle: <see cref="Start"/> → <see cref="ExposureState.Capturing"/> → optional
    /// <see cref="Pause"/>/<see cref="Resume"/> → <see cref="Stop"/> or <see cref="Export"/>.
    /// <see cref="OnAutoHalt"/> fires when a timer or sample-count stop policy is satisfied.</para>
    /// </summary>
    internal sealed class ViewportExposureAccumulator : IGameplayExposureAccumulator, IRenderer
    {
        private readonly ICoreClientAPI _capi;
        private readonly WetplateEffectsConfig _baselineEffects;
        private ExposureAccumulationBuffer? _buffer;
        private PlateProcessProfile _process;
        private ExposureStartOptions _startOptions;
        private float _elapsedSinceLastSample;
        private float _elapsedCaptureSeconds;
        private bool _rendererRegistered;
        private bool _disposed;
        private long _lastPreviewMs;

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
            _buffer?.Dispose();
            _buffer = new ExposureAccumulationBuffer(w, h, process.SampleCount);
            ApplyProcessToBuffer(_buffer, process);

            RegisterRenderer();
            State = ExposureState.Capturing;
            ViewportExposureSuppressContext.SuppressLocalPlayer = true;
        }

        public void Pause()
        {
            if (State == ExposureState.Capturing)
            {
                State = ExposureState.Paused;
                ViewportExposureSuppressContext.SuppressLocalPlayer = false;
            }
        }

        public void Resume()
        {
            if (State == ExposureState.Paused)
            {
                State = ExposureState.Capturing;
                ViewportExposureSuppressContext.SuppressLocalPlayer = true;
            }
        }

        public void Stop()
        {
            // Done: renderer unregistration was already deferred via EnqueueMainThreadTask in the
            // auto-halt path; calling UnregisterRenderer() here from within the render loop would
            // crash the game. Idle: nothing to do.
            if (State == ExposureState.Idle || State == ExposureState.Done) return;
            ViewportExposureSuppressContext.SuppressLocalPlayer = false;
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
            using SKBitmap flipped = FlipVertical(developed);
            SKBitmap cropped = PhotoCaptureRenderer.ScaleDownAndCenterCropToPlateAspect(flipped, maxDim);

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
        /// Develops a snapshot of the current accumulation for the preview overlay, throttled to at most
        /// one develop per <paramref name="refreshMs"/> milliseconds.
        /// Returns <see langword="false"/> when no frames are accumulated, state is not active, or the throttle has not elapsed.
        /// </summary>
        internal bool TryPeekDevelopedFrame(long nowMs, int refreshMs, int maxDimension, out int[] bgra, out int w, out int h)
        {
            bgra = Array.Empty<int>();
            w = 0;
            h = 0;
            if (_buffer == null || _buffer.FramesAccumulated == 0) return false;
            if (State != ExposureState.Capturing && State != ExposureState.Paused) return false;
            if (nowMs - _lastPreviewMs < refreshMs) return false;
            _lastPreviewMs = nowMs;

            using SKBitmap developed = _buffer.Develop();
            using SKBitmap flipped = FlipVertical(developed);
            using SKBitmap cropped = PhotoCaptureRenderer.ScaleDownAndCenterCropToPlateAspect(flipped, maxDimension);
            w = cropped.Width;
            h = cropped.Height;
            bgra = new int[w * h];
            System.Runtime.InteropServices.Marshal.Copy(cropped.GetPixels(), bgra, 0, w * h);
            return true;
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (State != ExposureState.Capturing || _buffer == null) return;

            _elapsedCaptureSeconds += deltaTime;
            if (_startOptions.StopMode == ExposureStopMode.Timer && _elapsedCaptureSeconds >= _startOptions.StopAfterSeconds)
            {
                CompleteAutoStop();
                return;
            }

            _elapsedSinceLastSample += deltaTime;
            if (_elapsedSinceLastSample < _process.SampleInterval) return;
            _elapsedSinceLastSample -= _process.SampleInterval;

            int w = _capi.Render.FrameWidth;
            int h = _capi.Render.FrameHeight;

            // Reset buffer on viewport resize to prevent averaging mixed-dimension frames.
            if (_buffer.Width != w || _buffer.Height != h)
            {
                _buffer.Dispose();
                _buffer = new ExposureAccumulationBuffer(w, h, _process.SampleCount);
                ApplyProcessToBuffer(_buffer, _process);
                _capi.Logger.Warning("Phototesting: viewport resized during exposure — accumulated frames discarded.");
            }

            int byteCount = w * h * 4;
            byte[] pixels = ArrayPool<byte>.Shared.Rent(byteCount);
            try
            {
                GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
                GL.ReadBuffer(ReadBufferMode.Back);
                GL.PixelStore(PixelStoreParameter.PackAlignment, 1);
                GL.ReadPixels(0, 0, w, h, PixelFormat.Bgra, PixelType.UnsignedByte, pixels);

                // Ensure fully opaque alpha so the accumulator treats every pixel as solid.
                for (int i = 3; i < byteCount; i += 4) pixels[i] = 255;

                if (_buffer is ICpuExposureAccumulator cpu)
                    cpu.Accumulate(pixels, w, h);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(pixels);
            }

            // Auto-halt once the target sample count is reached.
            // Do NOT call Stop() here — UnregisterRenderer() must not be called from within
            // a renderer iteration loop (causes ArgumentOutOfRangeException). Instead, set
            // Done immediately to stop accumulating and defer the unregister to the next frame.
            if (_startOptions.StopMode == ExposureStopMode.TargetSamples && _buffer.FramesAccumulated >= _process.SampleCount)
                CompleteAutoStop();
        }

        private void CompleteAutoStop()
        {
            ViewportExposureSuppressContext.SuppressLocalPlayer = false;
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
            buf.LinearizeInput = true;
            buf.ApplySpectralWeights = true;
            buf.ApplyHDCurve = true;
            buf.RedSensitivity = process.RedSensitivity;
            buf.GreenSensitivity = process.GreenSensitivity;
            buf.BlueSensitivity = process.BlueSensitivity;
            buf.DevelopmentStrength = process.DevelopmentStrength;
            buf.HDGamma = process.HDGamma;
        }

        // Flips a bitmap vertically using a canvas transform.
        // GL.ReadPixels returns bottom-to-top; this corrects the orientation.
        private static SKBitmap FlipVertical(SKBitmap src)
        {
            var dst = new SKBitmap(new SKImageInfo(src.Width, src.Height, SKColorType.Bgra8888, SKAlphaType.Opaque));
            using var canvas = new SKCanvas(dst);
            canvas.Scale(1, -1);
            canvas.Translate(0, -(float)src.Height);
            canvas.DrawBitmap(src, 0, 0);
            return dst;
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
            ViewportExposureSuppressContext.SuppressLocalPlayer = false;
            UnregisterRenderer();
            _buffer?.Dispose();
            _buffer = null;
            State = ExposureState.Idle;
        }
    }
}
