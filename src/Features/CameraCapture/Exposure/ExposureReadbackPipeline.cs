using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using Vintagestory.API.Client;

namespace Phototesting.CameraCapture.Exposure
{
    // GPU-side downsample + async PBO readback pipeline for the virtual exposure renderer.
    //
    // Each exposure sample goes through three stages:
    //   1. GPU blit-downsample: source (window-sized) FBO → small internal FBO (Y-flipped to
    //      fix OpenGL's bottom-left origin, eliminating the Skia rotate/mirror pass).
    //   2. Async ReadPixels into a PBO (no CPU stall; GPU DMA in background).
    //   3. Map + copy from the PBO written two kicks ago (guaranteed ready, no wait).
    //
    // The 3-PBO ring ensures that at kick N we only map the PBO from kick N-2, which
    // the GPU has had two full frames to finish writing. The first two kicks prime the
    // pipeline and produce no output; wall-clock duration (Fix A) handles total shutter time.
    //
    // Call EnsureAllocated() before each kick (no-ops when dimensions are unchanged).
    // Call KickAndCollect() once per sample; it returns true when outBytes is filled.
    // Call DrainPending() on shutter close to flush in-flight PBOs.
    // Call Dispose() when the session ends (called from VirtualExposureRenderer.Discard).
    internal sealed class ExposureReadbackPipeline : IDisposable
    {
        private readonly ICoreClientAPI _capi;

        // Downsample target — a small single-colour-attachment FBO.
        private FrameBufferRef? _downsampleFbo;
        private int _allocatedSourceW;
        private int _allocatedSourceH;
        private int _allocatedMaxDim;

        // 3-PBO async readback ring.
        private const int RingSize = 3;
        private readonly int[] _pboIds  = new int[RingSize];
        private readonly bool[] _pboPending = new bool[RingSize];
        private int _kickCount;   // total kicks issued so far; used to derive ring indices
        private int _pboByteSize;
        private bool _pbosAllocated;

        private bool _disposed;

        internal int Width  { get; private set; }
        internal int Height { get; private set; }

        internal ExposureReadbackPipeline(ICoreClientAPI capi)
        {
            _capi = capi;
        }

        internal static void ComputeTargetDimensions(int sourceW, int sourceH, int maxDim, out int width, out int height)
        {
            float scale = (float)maxDim / Math.Max(sourceW, sourceH);
            scale = Math.Min(1f, scale);
            width = Math.Max(1, (int)(sourceW * scale));
            height = Math.Max(1, (int)(sourceH * scale));
        }

        // Ensures the downsample FBO and PBO ring are sized for the given source and max-dim.
        // Returns true when the target dimensions changed (caller should reset the accumulation buffer).
        // Safe to call every sample — is a no-op when all three inputs are unchanged.
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
                    () => _capi.Render.DestroyFrameBuffer(_downsampleFbo));
                _downsampleFbo = null;
            }

            // Compute target dimensions preserving aspect ratio, clamped to source size.
            ComputeTargetDimensions(sourceW, sourceH, maxDim, out int width, out int height);
            Width = width;
            Height = height;

            // Single colour attachment (RGBA8, linear filter for the bilinear downsample).
            _downsampleFbo = ClientFramebufferCompat.Create(_capi,
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

        // Blits fromFbo → downsample FBO (Y-flipped), issues an async ReadPixels into the write
        // PBO, and maps the PBO from two kicks ago to fill outBytes.
        // Returns true when outBytes has been filled with a complete frame.
        // outBytes must be at least Width * Height * 4 bytes.
        internal bool KickAndCollect(FrameBufferRef fromFbo, byte[] outBytes)
        {
            if (_downsampleFbo == null || !_pbosAllocated) return false;

            // Save current GL framebuffer bindings so we can restore them after.
            GL.GetInteger(GetPName.ReadFramebufferBinding, out int prevRead);
            GL.GetInteger(GetPName.DrawFramebufferBinding, out int prevDraw);
            GL.GetInteger(GetPName.PixelPackBufferBinding, out int prevPbo);

            try
            {
                // --- Stage 1: blit-downsample with Y-flip ---
                // The Y-flip (dstY0=Height, dstY1=0) converts OpenGL's bottom-left-origin image
                // to top-left origin, replacing the Skia rotate+mirror pass in ReadFramebuffer.
                GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, fromFbo.FboId);
                GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _downsampleFbo.FboId);
                GL.BlitFramebuffer(
                    0, 0, fromFbo.Width, fromFbo.Height,
                    0, Height, Width, 0,                   // dst Y inverted = vertical flip
                    ClearBufferMask.ColorBufferBit,
                    BlitFramebufferFilter.Linear);

                // --- Stage 2: async ReadPixels into write PBO (no CPU stall) ---
                int writeIdx = _kickCount % RingSize;
                GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _downsampleFbo.FboId);
                GL.BindBuffer(BufferTarget.PixelPackBuffer, _pboIds[writeIdx]);
                GL.ReadPixels(0, 0, Width, Height, PixelFormat.Bgra, PixelType.UnsignedByte, IntPtr.Zero);
                _pboPending[writeIdx] = true;

                // --- Stage 3: map the PBO from two kicks ago (guaranteed done by GPU) ---
                bool produced = false;
                if (_kickCount >= RingSize - 1)
                {
                    // (kickCount + 1) % RingSize is the PBO written at kick (kickCount - 2).
                    int readIdx = (_kickCount + 1) % RingSize;
                    if (_pboPending[readIdx])
                    {
                        GL.BindBuffer(BufferTarget.PixelPackBuffer, _pboIds[readIdx]);
                        IntPtr mapped = GL.MapBuffer(BufferTarget.PixelPackBuffer, BufferAccess.ReadOnly);
                        if (mapped != IntPtr.Zero)
                        {
                            Marshal.Copy(mapped, outBytes, 0, _pboByteSize);
                            GL.UnmapBuffer(BufferTarget.PixelPackBuffer);
                            produced = true;
                        }
                        _pboPending[readIdx] = false;
                    }
                }

                _kickCount++;
                return produced;
            }
            finally
            {
                GL.BindBuffer(BufferTarget.PixelPackBuffer, prevPbo);
                GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, prevRead);
                GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, prevDraw);
            }
        }

        // Maps and copies all still-pending PBOs in ring order, invoking onFrame for each.
        // Call on shutter close before transitioning to Done to avoid losing the last 1–2 samples.
        // MapBuffer may briefly stall if a PBO hasn't finished yet — that's acceptable at shutter close.
        // scratch must be at least Width * Height * 4 bytes.
        internal void DrainPending(byte[] scratch, Action<byte[]> onFrame)
        {
            if (!_pbosAllocated) return;

            GL.GetInteger(GetPName.PixelPackBufferBinding, out int prevPbo);
            try
            {
                for (int i = 0; i < RingSize; i++)
                {
                    // Drain starting from the oldest pending PBO.
                    int idx = (_kickCount + 1 + i) % RingSize;
                    if (!_pboPending[idx]) continue;

                    GL.BindBuffer(BufferTarget.PixelPackBuffer, _pboIds[idx]);
                    IntPtr mapped = GL.MapBuffer(BufferTarget.PixelPackBuffer, BufferAccess.ReadOnly);
                    if (mapped != IntPtr.Zero)
                    {
                        Marshal.Copy(mapped, scratch, 0, _pboByteSize);
                        GL.UnmapBuffer(BufferTarget.PixelPackBuffer);
                        onFrame(scratch);
                    }
                    _pboPending[idx] = false;
                }
            }
            finally
            {
                GL.BindBuffer(BufferTarget.PixelPackBuffer, prevPbo);
            }
        }

        // Discards any in-flight ring state so the next EnsureAllocated/KickAndCollect starts clean.
        // Called when the accumulation buffer is reset mid-session.
        internal void ResetRing()
        {
            for (int i = 0; i < RingSize; i++) _pboPending[i] = false;
            _kickCount = 0;
        }

        private void AllocatePbos()
        {
            FreePbos();
            int byteSize = Width * Height * 4;
            GL.GenBuffers(RingSize, _pboIds);
            for (int i = 0; i < RingSize; i++)
            {
                GL.BindBuffer(BufferTarget.PixelPackBuffer, _pboIds[i]);
                GL.BufferData(BufferTarget.PixelPackBuffer, byteSize, IntPtr.Zero, BufferUsageHint.StreamRead);
                _pboPending[i] = false;
            }
            GL.BindBuffer(BufferTarget.PixelPackBuffer, 0);
            _pboByteSize   = byteSize;
            _kickCount     = 0;
            _pbosAllocated = true;
        }

        private void FreePbos()
        {
            if (!_pbosAllocated) return;
            for (int i = 0; i < RingSize; i++) _pboPending[i] = false;
            GL.DeleteBuffers(RingSize, _pboIds);
            Array.Clear(_pboIds, 0, RingSize);
            _pboByteSize   = 0;
            _kickCount     = 0;
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
                    () => _capi.Render.DestroyFrameBuffer(_downsampleFbo));
                _downsampleFbo = null;
            }
        }
    }
}
