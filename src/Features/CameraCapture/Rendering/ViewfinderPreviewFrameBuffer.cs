using System.Runtime.InteropServices;
using SkiaSharp;
using Phototesting.ImageEffects;

namespace Phototesting.CameraCapture.Rendering
{
    // Queued debug-preview request and latest-frame buffering for viewfinder diagnostics.
    // Keeps preview buffering concerns separate from the capture renderer pipeline.
    internal sealed class ViewfinderPreviewFrameBuffer
    {
        private readonly object _previewLock = new object();
        private (int MaxDimension, WetplateEffectsConfig? EffectsOverride)? _pendingPreviewRequest;
        private PreviewFrame? _latestPreviewFrame;

        private sealed class PreviewFrame
        {
            public readonly int[] BgraPixels;
            public readonly int Width;
            public readonly int Height;

            public PreviewFrame(int[] bgraPixels, int width, int height)
            {
                BgraPixels = bgraPixels;
                Width = width;
                Height = height;
            }
        }

        // Queues one preview capture request, replacing any older pending request.
        internal void RequestCapture(int maxDimension, WetplateEffectsConfig? effectsOverride = null)
        {
            lock (_previewLock)
            {
                _pendingPreviewRequest = (maxDimension, effectsOverride);
            }
        }

        // Returns and clears the current pending preview request.
        internal (int MaxDimension, WetplateEffectsConfig? EffectsOverride)? TakePendingCaptureRequest()
        {
            lock (_previewLock)
            {
                var pending = _pendingPreviewRequest;
                _pendingPreviewRequest = null;
                return pending;
            }
        }

        // Stores a processed preview frame in managed BGRA pixel format.
        internal void StoreFrame(SKBitmap bitmap)
        {
            if (bitmap == null || bitmap.Width <= 0 || bitmap.Height <= 0) return;

            int count = bitmap.Width * bitmap.Height;
            int[] pixels = new int[count];
            Marshal.Copy(bitmap.GetPixels(), pixels, 0, count);

            lock (_previewLock)
            {
                _latestPreviewFrame = new PreviewFrame(pixels, bitmap.Width, bitmap.Height);
            }
        }

        // Returns and clears the most recently stored preview frame.
        internal bool TryConsumeLatestFrame(out int[] bgraPixels, out int width, out int height)
        {
            lock (_previewLock)
            {
                if (_latestPreviewFrame == null)
                {
                    bgraPixels = Array.Empty<int>();
                    width = 0;
                    height = 0;
                    return false;
                }

                bgraPixels = _latestPreviewFrame.BgraPixels;
                width = _latestPreviewFrame.Width;
                height = _latestPreviewFrame.Height;
                _latestPreviewFrame = null;
                return true;
            }
        }

        // Clears pending requests and buffered preview frames during renderer disposal.
        internal void Clear()
        {
            lock (_previewLock)
            {
                _pendingPreviewRequest = null;
                _latestPreviewFrame = null;
            }
        }
    }
}
