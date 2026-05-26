using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using Vintagestory.API.Client;

namespace Phototesting.CameraCapture.Exposure
{
    /// <summary>
    /// GPU-side downsample and async PBO readback pipeline used by <see cref="VirtualExposureRenderer"/>.
    /// Each sample passes through three stages: a GPU blit-downsample into a small internal FBO
    /// (Y-flipped to fix OpenGL's bottom-left origin), an async <c>ReadPixels</c> into a PBO, and a
    /// map-and-copy from the PBO issued two kicks prior (guaranteed ready, no CPU stall).
    /// The three-slot ring means the first two kicks prime the pipeline and produce no output;
    /// total shutter wall-clock time is tracked separately in <see cref="VirtualExposureRenderer"/>.
    /// </summary>
    internal sealed class ExposureReadbackPipeline : IDisposable
    {
        private readonly ICoreClientAPI _clientApi;

        // Downsample target — a small single-colour-attachment FBO.
        private FrameBufferRef? _downsampleFbo;
        private int _allocatedSourceW;
        private int _allocatedSourceH;
        private int _allocatedMaxDim;

        // 3-PBO async readback ring.
        private const int RingSize = 3;
        private readonly int[] _pboIds  = new int[RingSize];
        private readonly bool[] _pboReadbackReady = new bool[RingSize];
        private int _totalKicksIssued;   // total kicks issued so far; used to derive ring indices
        private int _pboByteSize;
        private bool _pbosAllocated;

        private bool _disposed;

        internal int Width  { get; private set; }
        internal int Height { get; private set; }

        internal ExposureReadbackPipeline(ICoreClientAPI capi)
        {
            _clientApi = capi;
        }

        internal static void ComputeTargetDimensions(int sourceW, int sourceH, int maxDim, out int width, out int height)
        {
            float scale = (float)maxDim / Math.Max(sourceW, sourceH);
            scale = Math.Min(1f, scale);
            width = Math.Max(1, (int)(sourceW * scale));
            height = Math.Max(1, (int)(sourceH * scale));
        }

        /// <summary>
        /// Ensures the downsample FBO and PBO ring are sized for the given source dimensions and max target size.
        /// Returns <see langword="true"/> when the target dimensions changed (caller should reset the accumulation buffer).
        /// Safe to call every sample — is a no-op when all three inputs are unchanged.
        /// </summary>
        internal bool EnsureAllocated(int sourceW, int sourceH, int maxDim)
        {
            if (_allocatedSourceW == sourceW &&
                _allocatedSourceH == sourceH &&
                _allocatedMaxDim  == maxDim)
                return false;

            // Destroy old resources.
            FreePbos();
            if (_downsampleFbo != null)
            {
                BestEffort.Try(null, "destroy exposure downsample FBO",
                    () => _clientApi.Render.DestroyFrameBuffer(_downsampleFbo));
                _downsampleFbo = null;
            }

            // Compute target dimensions preserving aspect ratio, clamped to source size.
            ComputeTargetDimensions(sourceW, sourceH, maxDim, out int width, out int height);
            Width = width;
            Height = height;

            // Single colour attachment (RGBA8, linear filter for the bilinear downsample).
            _downsampleFbo = ClientFramebufferCompat.Create(_clientApi,
                new FramebufferAttrs("phototesting-exposure-downsample", Width, Height)
                {
                    Attachments = new[]
                    {
                        new FramebufferAttrsAttachment
                        {
                            AttachmentType = EnumFramebufferAttachment.ColorAttachment0,
                            Texture = new RawTexture
                            {
                                Width               = Width,
                                Height              = Height,
                                PixelInternalFormat = EnumTextureInternalFormat.Rgba8,
                                PixelFormat         = EnumTexturePixelFormat.Rgba,
                                MinFilter           = EnumTextureFilter.Linear,
                                MagFilter           = EnumTextureFilter.Linear
                            }
                        }
                    }
                });

            _allocatedSourceW = sourceW;
            _allocatedSourceH = sourceH;
            _allocatedMaxDim  = maxDim;

            AllocatePbos();
            return true;
        }

        /// <summary>
        /// Blits <paramref name="fromFbo"/> into the downsample FBO, issues an async <c>ReadPixels</c> into the
        /// write PBO, and maps the PBO from two kicks ago into <paramref name="outBytes"/>.
        /// Returns <see langword="true"/> when <paramref name="outBytes"/> has been filled with a complete BGRA frame.
        /// <paramref name="outBytes"/> must be at least <see cref="Width"/> × <see cref="Height"/> × 4 bytes.
        /// </summary>
        internal bool SubmitFrameAndCollectReadback(FrameBufferRef fromFbo, byte[] outBytes)
        {
            if (_downsampleFbo == null || !_pbosAllocated) return false;

            // Save current GL framebuffer bindings so we can restore them after.
            GL.GetInteger(GetPName.ReadFramebufferBinding, out int prevRead);
            GL.GetInteger(GetPName.DrawFramebufferBinding, out int prevDraw);
            GL.GetInteger(GetPName.PixelPackBufferBinding, out int prevPbo);

            try
            {
                // --- Stage 1: blit-downsample with Y-flip ---
                // The Y-flip converts OpenGL's bottom-left-origin image to top-left origin,
                // replacing the Skia rotate+mirror pass in ReadFramebuffer.
                ExposureUtils.BlitYFlipped(fromFbo, _downsampleFbo!);

                // --- Stage 2: async ReadPixels into write PBO (no CPU stall) ---
                int writeIdx = _totalKicksIssued % RingSize;
                GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _downsampleFbo.FboId);
                GL.BindBuffer(BufferTarget.PixelPackBuffer, _pboIds[writeIdx]);
                GL.ReadPixels(0, 0, Width, Height, PixelFormat.Bgra, PixelType.UnsignedByte, IntPtr.Zero);
                _pboReadbackReady[writeIdx] = true;

                // --- Stage 3: map the PBO from two kicks ago (guaranteed done by GPU) ---
                bool produced = false;
                if (_totalKicksIssued >= RingSize - 1)
                {
                    // (kickCount + 1) % RingSize is the PBO written at kick (kickCount - 2).
                    int readIdx = (_totalKicksIssued + 1) % RingSize;
                    if (_pboReadbackReady[readIdx])
                    {
                        GL.BindBuffer(BufferTarget.PixelPackBuffer, _pboIds[readIdx]);
                        IntPtr mapped = GL.MapBuffer(BufferTarget.PixelPackBuffer, BufferAccess.ReadOnly);
                        if (mapped != IntPtr.Zero)
                        {
                            Marshal.Copy(mapped, outBytes, 0, _pboByteSize);
                            GL.UnmapBuffer(BufferTarget.PixelPackBuffer);
                            produced = true;
                        }
                        _pboReadbackReady[readIdx] = false;
                    }
                }

                _totalKicksIssued++;
                return produced;
            }
            finally
            {
                GL.BindBuffer(BufferTarget.PixelPackBuffer, prevPbo);
                GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, prevRead);
                GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, prevDraw);
            }
        }

        /// <summary>
        /// Maps and copies all still-pending PBOs, invoking <paramref name="onFrame"/> for each.
        /// Call on shutter close before transitioning to <see cref="ExposureState.Done"/> to avoid losing the last 1–2 samples.
        /// May briefly stall if a PBO has not finished yet — acceptable at shutter close.
        /// <paramref name="scratch"/> must be at least <see cref="Width"/> × <see cref="Height"/> × 4 bytes.
        /// </summary>
        internal void DrainPending(byte[] scratch, Action<byte[]> onReadbackComplete)
        {
            if (!_pbosAllocated) return;

            GL.GetInteger(GetPName.PixelPackBufferBinding, out int prevPbo);
            try
            {
                for (int i = 0; i < RingSize; i++)
                {
                    // Drain starting from the oldest pending PBO.
                    int idx = (_totalKicksIssued + 1 + i) % RingSize;
                    if (!_pboReadbackReady[idx]) continue;

                    GL.BindBuffer(BufferTarget.PixelPackBuffer, _pboIds[idx]);
                    IntPtr mapped = GL.MapBuffer(BufferTarget.PixelPackBuffer, BufferAccess.ReadOnly);
                    if (mapped != IntPtr.Zero)
                    {
                        Marshal.Copy(mapped, scratch, 0, _pboByteSize);
                        GL.UnmapBuffer(BufferTarget.PixelPackBuffer);
                        onReadbackComplete(scratch);
                    }
                    _pboReadbackReady[idx] = false;
                }
            }
            finally
            {
                GL.BindBuffer(BufferTarget.PixelPackBuffer, prevPbo);
            }
        }

        /// <summary>Discards in-flight ring state so the next kick starts clean. Called when the accumulation buffer is reset mid-session.</summary>
        internal void ResetReadbackRing()
        {
            for (int i = 0; i < RingSize; i++) _pboReadbackReady[i] = false;
            _totalKicksIssued = 0;
        }

        private void AllocatePbos()
        {
            int byteSize = Width * Height * 4;
            GL.GenBuffers(RingSize, _pboIds);
            for (int i = 0; i < RingSize; i++)
            {
                GL.BindBuffer(BufferTarget.PixelPackBuffer, _pboIds[i]);
                GL.BufferData(BufferTarget.PixelPackBuffer, byteSize, IntPtr.Zero, BufferUsageHint.StreamRead);
                _pboReadbackReady[i] = false;
            }
            GL.BindBuffer(BufferTarget.PixelPackBuffer, 0);
            _pboByteSize   = byteSize;
            _totalKicksIssued     = 0;
            _pbosAllocated = true;
        }

        private void FreePbos()
        {
            if (!_pbosAllocated) return;
            for (int i = 0; i < RingSize; i++) _pboReadbackReady[i] = false;
            GL.DeleteBuffers(RingSize, _pboIds);
            Array.Clear(_pboIds, 0, RingSize);
            _pboByteSize   = 0;
            _totalKicksIssued     = 0;
            _pbosAllocated = false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            FreePbos();
            if (_downsampleFbo != null)
            {
                BestEffort.Try(null, "destroy exposure downsample FBO",
                    () => _clientApi.Render.DestroyFrameBuffer(_downsampleFbo));
                _downsampleFbo = null;
            }
        }
    }
}
