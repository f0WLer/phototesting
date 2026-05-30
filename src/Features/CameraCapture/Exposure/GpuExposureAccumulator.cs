using OpenTK.Graphics.OpenGL;
using SkiaSharp;
using Vintagestory.API.Client;

namespace Phototesting.CameraCapture.Exposure
{
    /// <summary>
    /// GPU-side floating-point exposure accumulator that replaces the CPU float-array pipeline.
    /// Each sample is blitted Y-flipped into a staging FBO, then accumulated on the GPU via
    /// a GLSL shader into a pair of RGBA32F ping-pong framebuffers.
    /// <see cref="Develop"/> renders the accumulated sums through the spectral-weights and H&amp;D
    /// GLSL shader into an RGBA8 FBO and reads it back synchronously — triggered only by
    /// preview cadence or shutter close, not per sample.
    /// All OpenGL state disturbed by public methods is saved and restored so VS's render pipeline is unaffected.
    /// </summary>
    internal sealed class GpuExposureAccumulator : IGpuExposureAccumulator
    {
        public int Width  { get; }
        public int Height { get; }
        public int FramesAccumulated => _frameCount;

        public bool  LinearizeInput          { get; set; } = true;
        public bool  ApplySpectralWeights    { get; set; } = true;
        public bool  ApplyHDCurve            { get; set; } = true;
        public bool  NormalizeByActualFrameCount { get; set; } = false;
        public float RedSensitivity          { get; set; } = 0.12f;
        public float GreenSensitivity        { get; set; } = 0.45f;
        public float BlueSensitivity         { get; set; } = 1.00f;
        public float DevelopmentStrength     { get; set; } = 3.5f;
        public float HDGamma                 { get; set; } = 1.1f;

        // Internal state.
        private readonly ICoreClientAPI _capi;
        private readonly int _targetSampleCount;
        private int _frameCount;

        // Sample FBO: RGBA8, receives the blit from the source camera FBO each frame.
        private FrameBufferRef _sampleFbo = null!;

        // Two RGBA32F accumulation FBOs (ping-pong).
        private readonly int[] _accumFboIds = new int[2];
        private readonly int[] _accumTexIds = new int[2];
        private int _readIdx  = 0;  // current accumulated sum
        private int _writeIdx = 1;  // scratch target for next add

        // Develop FBO: RGBA8, receives the final tone-mapped output for CPU readback.
        private FrameBufferRef _developFbo = null!;

        // OpenGL objects.
        private int _accumProgram;
        private int _developProgram;
        private int _quadVao;

        // Cached uniform locations for _accumProgram.
        private int _uAccumSample;
        private int _uAccumAccum;
        private int _uAccumLinearize;

        // Cached uniform locations for _developProgram.
        private int _uDevelopAccum;
        private int _uDevelopInvRef;
        private int _uDevelopSpectral;
        private int _uDevelopHdCurve;
        private int _uDevelopRedSens;
        private int _uDevelopGreenSens;
        private int _uDevelopBlueSens;
        private int _uDevelopDevStrength;
        private int _uDevelopGamma;

        private bool _disposed;

        // Vertex shader — fullscreen triangle from gl_VertexID, no VBO needed.
        // UVs (0,0)→(1,1) via the large-triangle trick; shared by both GPU programs.
        // Fragment shaders are loaded from embedded resources at construction time.
        private const string VertSrc = @"
#version 330 core
out vec2 v_uv;
void main() {
    float x = float((gl_VertexID & 1) == 0 ? 0 : 2);
    float y = float((gl_VertexID & 2) == 0 ? 0 : 2);
    v_uv        = vec2(x, y);
    gl_Position = vec4(x * 2.0 - 1.0, y * 2.0 - 1.0, 0.0, 1.0);
}";



        internal GpuExposureAccumulator(ICoreClientAPI capi, int width, int height, int referenceFrameCount)
        {
            _capi = capi;
            Width  = width;
            Height = height;
            _targetSampleCount = Math.Max(1, referenceFrameCount);

            AllocateGpuResources();
        }

        /// <summary>
        /// Accumulates from a raw GL framebuffer ID (e.g. the back buffer, ID 0).
        /// </summary>
        public void Accumulate(int sourceFboId, int sourceWidth, int sourceHeight)
        {
            if (_disposed) return;

            SaveGlState(out GlState state);
            try
            {
                DisableRenderStateForFullscreenPass();
                ExposureUtils.BlitYFlipped(sourceFboId, sourceWidth, sourceHeight, _sampleFbo);
                AccumulateFromSampleFbo();
            }
            finally
            {
                RestoreGlState(in state);
            }
        }

        public void Accumulate(FrameBufferRef sourceFbo)
        {
            if (_disposed) return;

            SaveGlState(out GlState state);
            try
            {
                DisableRenderStateForFullscreenPass();
                ExposureUtils.BlitYFlipped(sourceFbo, _sampleFbo);
                AccumulateFromSampleFbo();
            }
            finally
            {
                RestoreGlState(in state);
            }
        }

        // Runs the GLSL accumulate pass from _sampleFbo into the ping-pong accum FBOs.
        // GL state must already be saved and render state disabled before calling.
        private void AccumulateFromSampleFbo()
        {
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _accumFboIds[_writeIdx]);
            GL.Viewport(0, 0, Width, Height);

            GL.UseProgram(_accumProgram);
            GL.Uniform1(_uAccumSample,    0);
            GL.Uniform1(_uAccumAccum,     1);
            GL.Uniform1(_uAccumLinearize, LinearizeInput ? 1 : 0);

            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, _sampleFbo.ColorTextureIds[0]);
            GL.ActiveTexture(TextureUnit.Texture1);
            GL.BindTexture(TextureTarget.Texture2D, _accumTexIds[_readIdx]);

            GL.BindVertexArray(_quadVao);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

            (_readIdx, _writeIdx) = (_writeIdx, _readIdx);
            _frameCount++;
        }

        public void Reset()
        {
            if (_disposed) return;

            GL.GetInteger(GetPName.DrawFramebufferBinding, out int prevFbo);
            GL.ClearColor(0f, 0f, 0f, 0f);
            for (int i = 0; i < 2; i++)
            {
                GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _accumFboIds[i]);
                GL.Clear(ClearBufferMask.ColorBufferBit);
            }
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, prevFbo);

            _readIdx  = 0;
            _writeIdx = 1;
            _frameCount = 0;
        }

        public SKBitmap Develop()
        {
            if (_disposed || _frameCount == 0)
                return CreateBlackBitmap();

            SaveGlState(out GlState state);
            try
            {
                DisableRenderStateForFullscreenPass();

                // Normalise by actual or reference frame count.
                float invRef = NormalizeByActualFrameCount
                    ? 1f / _frameCount
                    : 1f / _targetSampleCount;

                // Normalise spectral weights so a grey pixel always maps to the same energy.
                float rw = RedSensitivity, gw = GreenSensitivity, bw = BlueSensitivity;
                float wSum = rw + gw + bw;
                if (wSum > 1e-6f) { rw /= wSum; gw /= wSum; bw /= wSum; }

                // Render accumulated sums → developed RGBA8 FBO.
                GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _developFbo.FboId);
                GL.Viewport(0, 0, Width, Height);

                GL.UseProgram(_developProgram);
                GL.Uniform1(_uDevelopAccum,       0);
                GL.Uniform1(_uDevelopInvRef,      invRef);
                GL.Uniform1(_uDevelopSpectral,    ApplySpectralWeights ? 1 : 0);
                GL.Uniform1(_uDevelopHdCurve,     ApplyHDCurve         ? 1 : 0);
                GL.Uniform1(_uDevelopRedSens,     rw);
                GL.Uniform1(_uDevelopGreenSens,   gw);
                GL.Uniform1(_uDevelopBlueSens,    bw);
                GL.Uniform1(_uDevelopDevStrength, DevelopmentStrength);
                GL.Uniform1(_uDevelopGamma,       HDGamma);

                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, _accumTexIds[_readIdx]);

                GL.BindVertexArray(_quadVao);
                GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

                // Synchronous readback — acceptable here since Develop() is not in the hot path.
                GL.Finish();
                return ClientFramebufferCapture.ReadToSkBitmap(_capi, _developFbo, flip: false);
            }
            finally
            {
                RestoreGlState(in state);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            FreeGpuResources();
        }

        private void AllocateGpuResources()
        {
            // Sample FBO: RGBA8, used as a staging area for each blit.
            _sampleFbo = ClientFramebufferCompat.Create(_capi,
                new FramebufferAttrs("phototesting-gpu-accu-sample", Width, Height)
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
                                MagFilter           = EnumTextureFilter.Linear,
                            }
                        }
                    }
                });

            // Develop FBO: RGBA8, receives the tone-mapped output for CPU readback.
            _developFbo = ClientFramebufferCompat.Create(_capi,
                new FramebufferAttrs("phototesting-gpu-accu-develop", Width, Height)
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
                                MinFilter           = EnumTextureFilter.Nearest,
                                MagFilter           = EnumTextureFilter.Nearest,
                            }
                        }
                    }
                });

            // Accumulation FBOs: RGBA32F ping-pong.
            // Created via raw GL since EnumTextureInternalFormat may not expose Rgba32f.
            GL.GenTextures(2, _accumTexIds);
            GL.GenFramebuffers(2, _accumFboIds);
            GL.GetInteger(GetPName.DrawFramebufferBinding, out int prevFbo);

            for (int i = 0; i < 2; i++)
            {
                GL.BindTexture(TextureTarget.Texture2D, _accumTexIds[i]);
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba32f,
                    Width, Height, 0, PixelFormat.Rgba, PixelType.Float, IntPtr.Zero);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
                GL.BindTexture(TextureTarget.Texture2D, 0);

                GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _accumFboIds[i]);
                GL.FramebufferTexture2D(FramebufferTarget.DrawFramebuffer,
                    FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _accumTexIds[i], 0);
                // Clear to zero so the first accumulate reads 0.0.
                GL.ClearColor(0f, 0f, 0f, 0f);
                GL.Clear(ClearBufferMask.ColorBufferBit);
            }
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, prevFbo);

            // Load fragment shaders from embedded resources and compile.
            string accumFrag   = LoadShaderSource("Phototesting.gpu-exposure-accum.frag.glsl");
            string developFrag = LoadShaderSource("Phototesting.gpu-exposure-develop.frag.glsl");
            _accumProgram   = CompileProgram(VertSrc, accumFrag);
            _developProgram = CompileProgram(VertSrc, developFrag);

            // Cache uniform locations (queried once here; zero-cost per draw call).
            _uAccumSample    = GL.GetUniformLocation(_accumProgram,   "u_sample");
            _uAccumAccum     = GL.GetUniformLocation(_accumProgram,   "u_accum");
            _uAccumLinearize = GL.GetUniformLocation(_accumProgram,   "u_linearize");

            _uDevelopAccum       = GL.GetUniformLocation(_developProgram, "u_accum");
            _uDevelopInvRef      = GL.GetUniformLocation(_developProgram, "u_inv_ref");
            _uDevelopSpectral    = GL.GetUniformLocation(_developProgram, "u_spectral");
            _uDevelopHdCurve     = GL.GetUniformLocation(_developProgram, "u_hd_curve");
            _uDevelopRedSens     = GL.GetUniformLocation(_developProgram, "u_red_sens");
            _uDevelopGreenSens   = GL.GetUniformLocation(_developProgram, "u_green_sens");
            _uDevelopBlueSens    = GL.GetUniformLocation(_developProgram, "u_blue_sens");
            _uDevelopDevStrength = GL.GetUniformLocation(_developProgram, "u_dev_strength");
            _uDevelopGamma       = GL.GetUniformLocation(_developProgram, "u_gamma");

            // Empty VAO for vertex-ID-based fullscreen draws.
            GL.GenVertexArrays(1, out _quadVao);
        }

        private void FreeGpuResources()
        {
            try { _capi.Render.DestroyFrameBuffer(_sampleFbo);  } catch { /* best-effort */ }
            try { _capi.Render.DestroyFrameBuffer(_developFbo); } catch { /* best-effort */ }

            for (int i = 0; i < 2; i++)
            {
                int fboId = _accumFboIds[i];
                int texId = _accumTexIds[i];
                try { GL.DeleteFramebuffer(fboId); } catch { /* best-effort */ }
                try { GL.DeleteTexture(texId);     } catch { /* best-effort */ }
            }

            try { GL.DeleteProgram(_accumProgram);   } catch { /* best-effort */ }
            try { GL.DeleteProgram(_developProgram); } catch { /* best-effort */ }
            try { GL.DeleteVertexArray(_quadVao);     } catch { /* best-effort */ }
        }

        private SKBitmap CreateBlackBitmap()
        {
            var info   = new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
            var bitmap = new SKBitmap(info);
            bitmap.Erase(SKColors.Black);
            return bitmap;
        }

        // ── GL state save/restore ─────────────────────────────────────────────────────────

        private readonly struct GlState
        {
            public readonly int DrawFbo;
            public readonly int ReadFbo;
            public readonly int Program;
            public readonly int Vao;
            public readonly int ActiveTexUnit;
            public readonly int Tex0;
            public readonly int Tex1;
            public readonly int[] Viewport;
            public readonly bool DepthTestEnabled;
            public readonly bool BlendEnabled;

            public GlState(int drawFbo, int readFbo, int program, int vao,
                int activeTexUnit, int tex0, int tex1, int[] viewport,
                bool depthTest, bool blend)
            {
                DrawFbo          = drawFbo;
                ReadFbo          = readFbo;
                Program          = program;
                Vao              = vao;
                ActiveTexUnit    = activeTexUnit;
                Tex0             = tex0;
                Tex1             = tex1;
                Viewport         = viewport;
                DepthTestEnabled = depthTest;
                BlendEnabled     = blend;
            }
        }

        private void SaveGlState(out GlState state)
        {
            GL.GetInteger(GetPName.DrawFramebufferBinding, out int drawFbo);
            GL.GetInteger(GetPName.ReadFramebufferBinding, out int readFbo);
            GL.GetInteger(GetPName.CurrentProgram,         out int prog);
            GL.GetInteger(GetPName.VertexArrayBinding,     out int vao);
            GL.GetInteger(GetPName.ActiveTexture,          out int activeUnit);

            GL.ActiveTexture(TextureUnit.Texture0);
            GL.GetInteger(GetPName.TextureBinding2D, out int tex0);
            GL.ActiveTexture(TextureUnit.Texture1);
            GL.GetInteger(GetPName.TextureBinding2D, out int tex1);

            var vp = new int[4];
            GL.GetInteger(GetPName.Viewport, vp);

            state = new GlState(drawFbo, readFbo, prog, vao, activeUnit, tex0, tex1, vp,
                GL.IsEnabled(EnableCap.DepthTest), GL.IsEnabled(EnableCap.Blend));
        }

        private static void RestoreGlState(in GlState s)
        {
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, s.DrawFbo);
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, s.ReadFbo);
            GL.UseProgram(s.Program);
            GL.BindVertexArray(s.Vao);

            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, s.Tex0);
            GL.ActiveTexture(TextureUnit.Texture1);
            GL.BindTexture(TextureTarget.Texture2D, s.Tex1);
            GL.ActiveTexture((TextureUnit)(s.ActiveTexUnit));

            GL.Viewport(s.Viewport[0], s.Viewport[1], s.Viewport[2], s.Viewport[3]);

            if (s.DepthTestEnabled) GL.Enable(EnableCap.DepthTest);
            else                    GL.Disable(EnableCap.DepthTest);

            if (s.BlendEnabled) GL.Enable(EnableCap.Blend);
            else                GL.Disable(EnableCap.Blend);
        }

        private static void DisableRenderStateForFullscreenPass()
        {
            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.Blend);
        }

        public byte[]? SerializeAccumulation()
        {
            if (_frameCount <= 0 || _disposed) return null;

            int pixelCount  = Width * Height;
            int floatCount  = pixelCount * 4;
            byte[] blob = new byte[ExposureAccumulationBlobFormat.GetTotalByteCount(Width, Height, 4)];

            ExposureAccumulationBlobFormat.WriteHeader(blob, Width, Height, 4, _frameCount, ExposureAccumulationBlobFormat.GpuBackend);

            SaveGlState(out GlState state);
            try
            {
                float[] floats = new float[floatCount];
                GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _accumFboIds[_readIdx]);
                GL.ReadPixels(0, 0, Width, Height, PixelFormat.Rgba, PixelType.Float, floats);
                System.Buffer.BlockCopy(floats, 0, blob, ExposureAccumulationBlobFormat.HeaderSize, floatCount * sizeof(float));
            }
            finally
            {
                RestoreGlState(in state);
            }

            return blob;
        }

        public bool DeserializeAccumulation(byte[] data, out int frameCount)
        {
            frameCount = 0;
            if (_disposed) return false;
            if (!ExposureAccumulationBlobFormat.TryReadHeader(data, out ExposureAccumulationBlobHeader header)) return false;
            if (header.Width != Width || header.Height != Height || header.ChannelCount != 4 || header.BackendTag != ExposureAccumulationBlobFormat.GpuBackend) return false;

            int floatCount = header.Width * header.Height * 4;
            int expected   = ExposureAccumulationBlobFormat.GetTotalByteCount(header.Width, header.Height, header.ChannelCount);
            if (data.Length < expected) return false;

            float[] floats = new float[floatCount];
            System.Buffer.BlockCopy(data, ExposureAccumulationBlobFormat.HeaderSize, floats, 0, floatCount * sizeof(float));

            SaveGlState(out GlState state);
            try
            {
                // Upload float data into the read accumulation texture and clear the write texture.
                GL.BindTexture(TextureTarget.Texture2D, _accumTexIds[_readIdx]);
                GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, Width, Height,
                    PixelFormat.Rgba, PixelType.Float, floats);

                GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _accumFboIds[_writeIdx]);
                GL.ClearColor(0f, 0f, 0f, 0f);
                GL.Clear(ClearBufferMask.ColorBufferBit);
            }
            finally
            {
                RestoreGlState(in state);
            }

            _frameCount = header.FrameCount;
            frameCount  = header.FrameCount;
            return true;
        }

        private static string LoadShaderSource(string resourceName)
        {
            var asm = typeof(GpuExposureAccumulator).Assembly;
            using var stream = asm.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded shader not found: {resourceName}");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private static int CompileProgram(string vertSrc, string fragSrc)
        {
            int vert = CompileShader(ShaderType.VertexShader,   vertSrc);
            int frag = CompileShader(ShaderType.FragmentShader, fragSrc);

            int prog = GL.CreateProgram();
            GL.AttachShader(prog, vert);
            GL.AttachShader(prog, frag);
            GL.LinkProgram(prog);
            GL.DetachShader(prog, vert);
            GL.DetachShader(prog, frag);
            GL.DeleteShader(vert);
            GL.DeleteShader(frag);

            GL.GetProgram(prog, GetProgramParameterName.LinkStatus, out int linkOk);
            if (linkOk == 0)
            {
                string log = GL.GetProgramInfoLog(prog);
                GL.DeleteProgram(prog);
                throw new InvalidOperationException($"Exposure accumulator shader link failed: {log}");
            }
            return prog;
        }

        private static int CompileShader(ShaderType type, string src)
        {
            int id = GL.CreateShader(type);
            GL.ShaderSource(id, src);
            GL.CompileShader(id);
            GL.GetShader(id, ShaderParameter.CompileStatus, out int ok);
            if (ok == 0)
            {
                string log = GL.GetShaderInfoLog(id);
                GL.DeleteShader(id);
                throw new InvalidOperationException($"Exposure accumulator {type} compilation failed: {log}");
            }
            return id;
        }
    }
}
