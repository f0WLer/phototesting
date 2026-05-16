using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;

namespace Phototesting.CameraCapture
{
    // Off-screen virtual camera backed by a dedicated OpenGL FBO.
    // Adapted from CamerasLib (Moby_). Oblique projection removed; FBO setup and render pipeline unchanged.
    internal sealed class VirtualCamera
    {
        private readonly ICoreClientAPI _capi;
        private readonly ClientPlatformWindows _platform;
        private readonly ClientMain _main;

        public Vec3d CameraPos = new Vec3d(0, 0, 0);
        public float Pitch = 0f;
        public float Yaw = 0f;
        public float Roll = 0f;

        public float ZNear = 0f;
        public float ZFar = 0f;
        public float Fov = 0f;

        public int Dimension = 0;

        public bool Rendering = true;

        public FrameBufferRef fbo = null!; // Assigned by InitBuffer() before first use

        internal VirtualCamera(ICoreClientAPI api, ClientPlatformWindows platform, ClientMain main)
        {
            _capi = api;
            _platform = platform;
            _main = main;
        }

        internal void Destroy()
        {
            _platform.DisposeFrameBuffer(fbo);
        }

        internal void InitBuffer()
        {
            fbo = new FrameBufferRef
            {
                FboId = GL.GenFramebuffer(),
                Width = _capi.Render.FrameWidth,
                Height = _capi.Render.FrameHeight,
                DepthTextureId = GL.GenTexture()
            };

            _platform.LoadFrameBuffer(fbo);

            // Depth.
            GL.BindTexture(TextureTarget.Texture2D, fbo.DepthTextureId);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.DepthComponent32, _capi.Render.FrameWidth, _capi.Render.FrameHeight, 0, PixelFormat.DepthComponent, PixelType.Float, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, 9728);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, 9728);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, 33071);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, 33071);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, fbo.DepthTextureId, 0);
            GL.DepthFunc(DepthFunction.Less);

            // Color 1.
            fbo.ColorTextureIds = ArrayUtil.CreateFilled(2, (int n) => GL.GenTexture());
            GL.BindTexture(TextureTarget.Texture2D, fbo.ColorTextureIds[0]);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, _capi.Render.FrameWidth, _capi.Render.FrameHeight, 0, PixelFormat.Rgba, PixelType.UnsignedShort, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, 9728);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, 9728);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, fbo.ColorTextureIds[0], 0);

            // Color 2.
            GL.BindTexture(TextureTarget.Texture2D, fbo.ColorTextureIds[1]);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, _capi.Render.FrameWidth, _capi.Render.FrameHeight, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, 9728);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, 9728);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment1, TextureTarget.Texture2D, fbo.ColorTextureIds[1], 0);

            // No SSAO, assign 2 color buffers.
            DrawBuffersEnum[] bufferEnums = new DrawBuffersEnum[2]
            {
                DrawBuffersEnum.ColorAttachment0,
                DrawBuffersEnum.ColorAttachment1
            };
            GL.DrawBuffers(2, bufferEnums);

            _platform.LoadFrameBuffer((ShaderProgramBase.CurrentShaderProgram?.PassName == "gui") ? EnumFrameBuffer.Default : EnumFrameBuffer.Primary);
        }

        internal void UpdateCamera()
        {
            _capi.World.Player.Entity.CameraPos = CameraPos.Clone();
            PlayerCamera camera = _main.MainCamera;
            Traverse cameraTraverse = Traverse.Create(camera);
            camera.CamSourcePosition.Set(CameraPos);
            camera.OriginPosition.Set(0, 0, 0);

            camera.Yaw = this.Yaw;
            camera.Pitch = this.Pitch;
            camera.Roll = this.Roll;

            float oldZNear = (float)cameraTraverse.Field("ZNear").GetValue();
            float oldZFar = (float)cameraTraverse.Field("ZFar").GetValue();
            float oldFov = (float)cameraTraverse.Field("Fov").GetValue();

            if (ZNear != 0) cameraTraverse.Field("ZNear").SetValue(ZNear);
            if (ZFar != 0) cameraTraverse.Field("ZFar").SetValue(ZFar);
            if (Fov != 0) cameraTraverse.Field("Fov").SetValue(Fov);

            cameraTraverse.Method("Update", float.Epsilon, _main.interesectionTester).GetValue();

            DefaultShaderUniforms shUniforms = _capi.Render.ShaderUniforms;

            if (shUniforms.playerReferencePos == null)
            {
                shUniforms.playerReferencePos = new Vec3d(_main.BlockAccessor.MapSizeX / 2, 0.0, _main.BlockAccessor.MapSizeZ / 2);
            }

            if ((double)shUniforms.playerReferencePos.HorizontalSquareDistanceTo(CameraPos.X, CameraPos.Z) > 400000000.0)
            {
                shUniforms.playerReferencePos.Set((float)CameraPos.X, 0.0, (float)CameraPos.Z);
            }

            shUniforms.PlayerPos.Set(
                (float)(CameraPos.X - shUniforms.playerReferencePos.X),
                (float)(CameraPos.Y - shUniforms.playerReferencePos.Y),
                (float)(CameraPos.Z - shUniforms.playerReferencePos.Z));

            _main.GlMatrixModeModelView();
            _main.GlLoadMatrix(cameraTraverse.Field("CameraMatrix").GetValue() as double[]);

            double[] top = _capi.Render.PMatrix.Top;
            double[] top2 = _capi.Render.MvMatrix.Top;

            _main.frustumCuller.CalcFrustumEquations(CameraPos.AsBlockPos, top, top2);

            for (int i = 0; i < 16; i++)
            {
                _main.PerspectiveProjectionMat[i] = top[i];
                _main.PerspectiveViewMat[i] = top2[i];
            }

            cameraTraverse.Field("ZNear").SetValue(oldZNear);
            cameraTraverse.Field("ZFar").SetValue(oldZFar);
            cameraTraverse.Field("Fov").SetValue(oldFov);
        }

        internal void RenderCamera(float dt)
        {
            FrameBufferRef currentFbo = _platform.CurrentFrameBuffer;
            FrameBufferRef primaryFbo = _platform.FrameBuffers[0];

            _main.PerspectiveMode();
            UpdateCamera();
            _main.Reset3DProjection();

            GL.Enable(EnableCap.DepthTest);

            _platform.CurrentFrameBuffer = fbo;
            _platform.FrameBuffers[0] = fbo;

            GL.ClipPlane(ClipPlaneName.ClipDistance0, new double[] { 0d, 1d, 0d, 5d });
            GL.Enable(EnableCap.ClipDistance0);

            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            _main.TriggerRenderStage(EnumRenderStage.Opaque, dt);

            ScreenManager.FrameProfiler.Mark("rendTransp-begin");
            _platform.LoadFrameBuffer(EnumFrameBuffer.Transparent);
            ScreenManager.FrameProfiler.Mark("rendTransp-fbloaded");
            _platform.ClearFrameBuffer(EnumFrameBuffer.Transparent);
            ScreenManager.FrameProfiler.Mark("rendTransp-bufscleared");
            _main.TriggerRenderStage(EnumRenderStage.OIT, dt);
            _platform.UnloadFrameBuffer(EnumFrameBuffer.Transparent);
            ScreenManager.FrameProfiler.Mark("rendTranspDone");
            _platform.MergeTransparentRenderPass();
            ScreenManager.FrameProfiler.Mark("mergeTranspPassDone");

            GL.Disable(EnableCap.ClipPlane0);
            GL.Disable(EnableCap.DepthTest);
            _platform.FrameBuffers[0] = primaryFbo;
            _platform.CurrentFrameBuffer = currentFbo;
        }
    }
}
