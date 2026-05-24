using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using SkiaSharp;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;
using Phototesting.AdminTooling;
using Phototesting.CameraCapture.Exposure;
using Phototesting.CameraCapture.Rendering;
using Phototesting.ImageEffects;

namespace Phototesting.CameraCapture
{
    // Persistent virtual-camera preview renderer.
    // In idle mode it borrows the prepared VirtualCamera from VirtualExposureRenderer (via
    // TryGetIdleCameraForPreview) and renders timed frames when DebugPreviewPeak is on.
    // In exposure mode (BeginExposurePassthrough / StoreExposureFrame / EndExposurePassthrough)
    // it passively buffers frames developed by VirtualExposureRenderer instead.
    // Registered at EnumRenderStage.Before so it can call TriggerRenderStage(Opaque) safely.
    internal sealed class VirtualCameraPreviewRenderer : IRenderer, IDisposable, IExposurePreviewSink
    {
        private readonly ICoreClientAPI _capi;
        private readonly ClientPlatformWindows _platform;
        private readonly ClientMain _main;
        private int[]? _latestPreviewPixels;
        private int _latestPreviewWidth;
        private int _latestPreviewHeight;

        private long _lastRenderMs;
        private bool _disposed;

        // The emulsion process used when developing idle-preview frames.
        // Updated to match the chosen process when an exposure session is started.
        internal PlateProcessProfile EmulsionProcess { get; set; } = PlateProcessProfile.Iodide;

        // Set by the bridge after both renderers are constructed.
        internal VirtualExposureRenderer? ExposureRenderer { get; set; }

        public double RenderOrder => 0.4;
        public int RenderRange => 0;

        // True when there is content to display: either exposure passthrough frames are active,
        // or the exposure renderer has a prepared idle camera available for preview.
        internal bool IsActive => _exposurePassthrough || (ExposureRenderer?.HasIdleCameraForPreview == true);

        // True while an exposure session is actively pushing accumulation frames.
        // Used by the overlay to bypass the DebugPreviewPeak gate during a live exposure.
        internal bool IsExposureActive => _exposurePassthrough;

        // Exposure mode reuses this overlay surface but supplies already-developed frames.
        private bool _exposurePassthrough;

        // Exposure preview takes ownership of this renderer's output surface.
        // The vcam (_camera) is left alive so subsequent exposures can reuse the same view.
        public void BeginExposurePassthrough()
        {
            _exposurePassthrough = true;
        }

        // Re-enables normal mode and clears the last pushed exposure frame.
        // If a vcam was active it resumes rendering automatically on the next tick.
        public void EndExposurePassthrough()
        {
            _exposurePassthrough = false;
            _latestPreviewPixels = null;
        }

        // Stores an already-developed exposure bitmap into the preview buffer.
        // Ignored unless BeginExposurePassthrough() was called.
        public void StoreExposureFrame(SKBitmap bmp)
        {
            if (!_exposurePassthrough) return;
            int count = bmp.Width * bmp.Height;
            int[] pixels = new int[count];
            Marshal.Copy(bmp.GetPixels(), pixels, 0, count);
            _latestPreviewPixels = pixels;
            _latestPreviewWidth = bmp.Width;
            _latestPreviewHeight = bmp.Height;
        }

        internal VirtualCameraPreviewRenderer(ICoreClientAPI capi)
        {
            _capi = capi;
            _main = (ClientMain)capi.World;
            _platform = (ClientPlatformWindows)_main.Platform;
        }

        // Returns and clears the most recently buffered preview frame, or false if none is available.
        internal bool TryConsumeLatestFrame(out int[] bgraPixels, out int width, out int height)
        {
            if (_latestPreviewPixels == null)
            {
                bgraPixels = Array.Empty<int>();
                width = 0;
                height = 0;
                return false;
            }
            bgraPixels = _latestPreviewPixels;
            width = _latestPreviewWidth;
            height = _latestPreviewHeight;
            _latestPreviewPixels = null;
            return true;
        }

        // Re-renders the virtual scene using the exposure renderer's idle camera and stores the
        // result when the refresh interval has elapsed. Gated on DebugPreviewPeak so idle preview
        // only runs when the user has explicitly turned on peak mode.
        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            // Exposure renderer is the frame source during an active exposure; nothing to render.
            if (_exposurePassthrough) return;
            if (ExposureRenderer == null) return;

            ViewfinderConfig? cfg = PhotoTestingConfigAccess.ResolveClientConfig(_capi)?.Viewfinder;
            if (!(cfg?.DebugPreviewPeak ?? false)) return;

            if (!ExposureRenderer.TryGetIdleCameraForPreview(out VirtualCamera camera)) return;

            int refreshMs = cfg!.DebugPreviewRefreshMs;
            long nowMs = _capi.ElapsedMilliseconds;
            if (nowMs - _lastRenderMs < refreshMs) return;
            _lastRenderMs = nowMs;

            // Reinitialize the FBO if the window was resized since last render.
            if (_capi.Render.FrameWidth != camera.fbo.Width || _capi.Render.FrameHeight != camera.fbo.Height)
            {
                camera.Destroy();
                camera.InitBuffer();
            }

            try
            {
                camera.RenderCameraInStoredDimension(deltaTime);

                // Clear the primary FBO that RenderCamera may have left in an intermediate state.
                GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

                int maxDimension = cfg.DebugPreviewMaxDimension;
                using SKBitmap raw = VirtualCaptureService.ReadFramebuffer(_capi, camera.fbo);
                using SKBitmap croppedBitmap = PhotoCropMath.ScaleDownAndCenterCropToPlateAspect(raw, maxDimension);

                EmulsionDevelop.ApplyInPlace(croppedBitmap, EmulsionProcess);

                int count = croppedBitmap.Width * croppedBitmap.Height;
                int[] pixels = new int[count];
                Marshal.Copy(croppedBitmap.GetPixels(), pixels, 0, count);
                _latestPreviewPixels = pixels;
                _latestPreviewWidth = croppedBitmap.Width;
                _latestPreviewHeight = croppedBitmap.Height;
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
            EndExposurePassthrough();
        }
    }
}
