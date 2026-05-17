using OpenTK.Graphics.OpenGL;
using SkiaSharp;
using Vintagestory.API.Client;

namespace Phototesting.CameraCapture.Exposure
{
    // GPU-side floating-point exposure accumulator.
    //
    // Replaces the CPU float-array pipeline with a pair of RGBA32F ping-pong framebuffers
    // that accumulate directly on the GPU.  Each sample goes through two passes:
    //
    //   Blit pass:   sourceFbo → _sampleFbo  (Y-flipped downsample, same as CPU path)
    //   Accumulate:  _sampleFbo + accum[read] → accum[write]  (custom GLSL, fullscreen triangle)
    //
    // Develop() renders the current accumulation through the H&D / spectral-weights GLSL
    // shader into an RGBA8 FBO and reads it back synchronously — this is a one-shot path
    // that is only triggered by preview cadence or shutter-close, not per sample.
    //
    // All GL state that is disturbed (FBO bindings, program, viewport, textures, depth
    // test, blending, VAO) is saved before and restored after each public method so that
    // VS's own rendering pipeline is unaffected.
    internal sealed class GpuExposureAccumulator : IGpuExposureAccumulator
    {
        // ── IExposureAccumulator properties ──────────────────────────────────────────────
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

        // ── Internal state ────────────────────────────────────────────────────────────────
        private readonly ICoreClientAPI _capi;
        private readonly int _referenceFrameCount;
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

        // Reused scratch storage to avoid per-call heap allocation.
        private readonly int[] _savedViewport = new int[4];

        private bool _disposed;

        // ── GLSL source (shared vertex shader + two fragment shaders) ─────────────────────
        //
        // Vertex: produces a fullscreen triangle from gl_VertexID, no VBO needed.
        // UVs (0,0)→(1,1) map directly onto the fullscreen quad via the large-triangle trick.
        private const string VertSrc = @"
#version 330 core
out vec2 v_uv;
void main() {
    float x = float((gl_VertexID & 1) == 0 ? 0 : 2);
    float y = float((gl_VertexID & 2) == 0 ? 0 : 2);
    v_uv        = vec2(x, y);
    gl_Position = vec4(x * 2.0 - 1.0, y * 2.0 - 1.0, 0.0, 1.0);
}";

        // Fragment: adds the new sample (optionally linearised) to the running RGBA32F sum.
        private const string AccumFragSrc = @"
#version 330 core
in  vec2 v_uv;
out vec4 out_sum;

uniform sampler2D u_sample;
uniform sampler2D u_accum;
uniform bool      u_linearize;

float srgbToLinear(float c) {
    return c <= 0.04045 ? c / 12.92 : pow((c + 0.055) / 1.055, 2.4);
}

void main() {
    vec3 s    = texture(u_sample, v_uv).rgb;
    vec3 prev = texture(u_accum,  v_uv).rgb;
    if (u_linearize)
        s = vec3(srgbToLinear(s.r), srgbToLinear(s.g), srgbToLinear(s.b));
    out_sum = vec4(prev + s, 1.0);
}";

        // Fragment: maps the accumulated sums through spectral-weights and H&D curve to RGBA8.
        // Mirrors ExposureAccumulationBuffer.Develop() exactly, including weight normalisation.
        private const string DevelopFragSrc = @"
#version 330 core
in  vec2 v_uv;
out vec4 out_color;

uniform sampler2D u_accum;
uniform float u_inv_ref;
uniform bool  u_spectral;
uniform bool  u_hd_curve;
uniform float u_red_sens;
uniform float u_green_sens;
uniform float u_blue_sens;
uniform float u_dev_strength;
uniform float u_gamma;

float hdCurve(float E, float k, float g) {
    float d = log(1.0 + E * k) / log(10.0);
    return pow(max(d, 0.0), g);
}

void main() {
    vec3 sum = texture(u_accum, v_uv).rgb;
    vec3 E   = sum * u_inv_ref;

    vec3 result;
    if (u_spectral) {
        float e = E.r * u_red_sens + E.g * u_green_sens + E.b * u_blue_sens;
        float v = u_hd_curve ? hdCurve(e, u_dev_strength, u_gamma) : e;
        result  = vec3(clamp(v, 0.0, 1.0));
    } else {
        if (u_hd_curve) {
            result = clamp(vec3(
                hdCurve(E.r, u_dev_strength, u_gamma),
                hdCurve(E.g, u_dev_strength, u_gamma),
                hdCurve(E.b, u_dev_strength, u_gamma)), 0.0, 1.0);
        } else {
            result = clamp(E, 0.0, 1.0);
        }
    }
    out_color = vec4(result, 1.0);
}";

        // ── Constructor ───────────────────────────────────────────────────────────────────
        internal GpuExposureAccumulator(ICoreClientAPI capi, int width, int height, int referenceFrameCount)
        {
            _capi = capi;
            Width  = width;
            Height = height;
            _referenceFrameCount = Math.Max(1, referenceFrameCount);

            AllocateGpuResources();
        }

        // ── IGpuExposureAccumulator ───────────────────────────────────────────────────────
        public void Accumulate(FrameBufferRef sourceFbo)
        {
            if (_disposed) return;

            SaveGlState(out GlState state);
            try
            {
                DisableRenderStateForFullscreenPass();

                // 1. Blit source → sample FBO (Y-flipped downsample, same as CPU path).
                GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, sourceFbo.FboId);
                GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _sampleFbo.FboId);
                GL.BlitFramebuffer(
                    0, 0, sourceFbo.Width, sourceFbo.Height,
                    0, Height, Width, 0,            // dst Y inverted = vertical flip
                    ClearBufferMask.ColorBufferBit,
                    BlitFramebufferFilter.Linear);

                // 2. Accumulate: sample + current accum → next accum.
                GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _accumFboIds[_writeIdx]);
                GL.Viewport(0, 0, Width, Height);

                GL.UseProgram(_accumProgram);
                GL.Uniform1(GL.GetUniformLocation(_accumProgram, "u_sample"), 0);
                GL.Uniform1(GL.GetUniformLocation(_accumProgram, "u_accum"),  1);
                GL.Uniform1(GL.GetUniformLocation(_accumProgram, "u_linearize"), LinearizeInput ? 1 : 0);

                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, _sampleFbo.ColorTextureIds[0]);
                GL.ActiveTexture(TextureUnit.Texture1);
                GL.BindTexture(TextureTarget.Texture2D, _accumTexIds[_readIdx]);

                GL.BindVertexArray(_quadVao);
                GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

                // Swap ping-pong.
                (_readIdx, _writeIdx) = (_writeIdx, _readIdx);
                _frameCount++;
            }
            finally
            {
                RestoreGlState(in state);
            }
        }

        // ── IExposureAccumulator ──────────────────────────────────────────────────────────
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
                    : 1f / _referenceFrameCount;

                // Normalise spectral weights so a grey pixel always maps to the same energy.
                float rw = RedSensitivity, gw = GreenSensitivity, bw = BlueSensitivity;
                float wSum = rw + gw + bw;
                if (wSum > 1e-6f) { rw /= wSum; gw /= wSum; bw /= wSum; }

                // Render accumulated sums → developed RGBA8 FBO.
                GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _developFbo.FboId);
                GL.Viewport(0, 0, Width, Height);

                GL.UseProgram(_developProgram);
                GL.Uniform1(GL.GetUniformLocation(_developProgram, "u_accum"),        0);
                GL.Uniform1(GL.GetUniformLocation(_developProgram, "u_inv_ref"),      invRef);
                GL.Uniform1(GL.GetUniformLocation(_developProgram, "u_spectral"),     ApplySpectralWeights ? 1 : 0);
                GL.Uniform1(GL.GetUniformLocation(_developProgram, "u_hd_curve"),     ApplyHDCurve         ? 1 : 0);
                GL.Uniform1(GL.GetUniformLocation(_developProgram, "u_red_sens"),     rw);
                GL.Uniform1(GL.GetUniformLocation(_developProgram, "u_green_sens"),   gw);
                GL.Uniform1(GL.GetUniformLocation(_developProgram, "u_blue_sens"),    bw);
                GL.Uniform1(GL.GetUniformLocation(_developProgram, "u_dev_strength"), DevelopmentStrength);
                GL.Uniform1(GL.GetUniformLocation(_developProgram, "u_gamma"),        HDGamma);

                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, _accumTexIds[_readIdx]);

                GL.BindVertexArray(_quadVao);
                GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

                // Synchronous readback — acceptable here since Develop() is not in the hot path.
                GL.Finish();
                return ReadbackBitmap();
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

        // ── Private helpers ───────────────────────────────────────────────────────────────

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

            // Compile shaders.
            _accumProgram   = CompileProgram(VertSrc, AccumFragSrc);
            _developProgram = CompileProgram(VertSrc, DevelopFragSrc);

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

            if (_accumProgram   != 0) try { GL.DeleteProgram(_accumProgram);      } catch { /* best-effort */ }
            if (_developProgram != 0) try { GL.DeleteProgram(_developProgram);    } catch { /* best-effort */ }
            if (_quadVao        != 0) try { GL.DeleteVertexArray(_quadVao);       } catch { /* best-effort */ }
        }

        private SKBitmap ReadbackBitmap()
        {
            return ClientFramebufferCapture.ReadToSkBitmap(_capi, _developFbo, flip: false);
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

        // ── Shader compilation ────────────────────────────────────────────────────────────

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
