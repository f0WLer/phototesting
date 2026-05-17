using System;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;

namespace Phototesting.CameraCapture
{
    // Complete pose/config snapshot for handing a virtual camera between preview,
    // exposure, and one-shot capture code without losing dimension or self-portrait state.
    internal readonly struct VirtualCameraState
    {
        internal readonly Vec3d Position;
        internal readonly float Yaw;
        internal readonly float Pitch;
        internal readonly float Fov;
        internal readonly int Dimension;
        internal readonly bool SelfPortrait;

        internal VirtualCameraState(Vec3d position, float yaw, float pitch, float fov, int dimension, bool selfPortrait = false)
        {
            Position = position.Clone();
            Yaw = yaw;
            Pitch = pitch;
            Fov = fov;
            Dimension = dimension;
            SelfPortrait = selfPortrait;
        }
    }

    // Off-screen virtual camera backed by a dedicated OpenGL FBO.
    // Adapted from CamerasLib (Moby_) - Oblique projection removed, FBO setup and render pipeline unchanged.
    internal sealed class VirtualCamera
    {
        private readonly struct SavedMainCameraState
        {
            internal readonly double Yaw;
            internal readonly double Pitch;
            internal readonly double Roll;
            internal readonly Vec3d CamSourcePos;
            internal readonly Vec3d OriginPos;
            internal readonly Vec3d EntityCameraPos;
            internal readonly Vec3f PlayerPos;
            internal readonly Vec3f PlayerPosForFoam;
            internal readonly Vec3d? PlayerReferencePos;
            internal readonly Vec3d? PlayerReferencePosForFoam;
            internal readonly bool PlayerWasRendered;

            internal SavedMainCameraState(PlayerCamera camera, EntityPlayer player, DefaultShaderUniforms shUniforms)
            {
                Yaw = camera.Yaw;
                Pitch = camera.Pitch;
                Roll = camera.Roll;
                CamSourcePos = camera.CamSourcePosition.Clone();
                OriginPos = camera.OriginPosition.Clone();
                EntityCameraPos = player.CameraPos.Clone();
                PlayerPos = shUniforms.PlayerPos.Clone();
                PlayerPosForFoam = shUniforms.PlayerPosForFoam.Clone();
                PlayerReferencePos = shUniforms.playerReferencePos?.Clone();
                PlayerReferencePosForFoam = shUniforms.playerReferencePosForFoam?.Clone();
                PlayerWasRendered = player.IsRendered;
            }
        }

        private readonly ICoreClientAPI _capi;
        private readonly ClientPlatformWindows _platform;
        private readonly ClientMain _main;

        public Vec3d CameraPos = new Vec3d(0, 0, 0);
        public float Pitch = 0f;
        public float Yaw = 0f;
        public float Roll = 0f;

        public float Fov = 0f;

        // Render the local player's full third-person model from this virtual viewpoint.
        public bool SelfPortrait = false;

        public int Dimension = 0;

        public FrameBufferRef fbo = null!; // Assigned by InitBuffer() before first use

        private const string CameraModeFieldName = "CameraMode";
        private const string RendererRenderModeFieldName = "renderMode";
        private const string ChunkRendererFieldName = "chunkRenderer";
        private const string ChunkRendererBeforeMethodName = "OnRenderBefore";
        private const int GlClampToBorder = 33069;

        // Cached GameContent enum value used to force the local player renderer out of first-person arms mode.
        private static Type? _renderModeType;
        private static object? _renderModeThirdPerson;

        internal VirtualCamera(ICoreClientAPI api, ClientPlatformWindows platform, ClientMain main)
        {
            _capi = api;
            _platform = platform;
            _main = main;
        }

        internal void Destroy()
        {
            if (fbo != null)
            {
                _capi.Render.DestroyFrameBuffer(fbo);
            }
        }

        internal void ApplyState(VirtualCameraState state)
        {
            CameraPos = state.Position.Clone();
            Yaw = state.Yaw;
            Pitch = state.Pitch;
            Fov = state.Fov;
            Dimension = state.Dimension;
            SelfPortrait = state.SelfPortrait;
        }

        internal VirtualCameraState GetState()
            => new VirtualCameraState(CameraPos, Yaw, Pitch, Fov, Dimension, SelfPortrait);

        internal void RenderCameraInStoredDimension(float dt)
        {
            int savedDimension = _capi.World.Player.Entity.Pos.Dimension;
            try
            {
                _capi.World.Player.Entity.Pos.Dimension = Dimension;
                RenderCamera(dt);
            }
            finally
            {
                _capi.World.Player.Entity.Pos.Dimension = savedDimension;
            }
        }

        internal void InitBuffer()
        {
            int width = _capi.Render.FrameWidth;
            int height = _capi.Render.FrameHeight;
            bool setupSsao = ClientSettings.SSAOQuality > 0;

            var attachments = new List<FramebufferAttrsAttachment>
            {
                Attachment(EnumFramebufferAttachment.DepthAttachment, width, height, EnumTextureInternalFormat.DepthComponent32, EnumTexturePixelFormat.DepthComponent, EnumTextureFilter.Nearest),
                Attachment(EnumFramebufferAttachment.ColorAttachment0, width, height, EnumTextureInternalFormat.Rgba8, EnumTexturePixelFormat.Rgba, EnumTextureFilter.Nearest),
                Attachment(EnumFramebufferAttachment.ColorAttachment1, width, height, EnumTextureInternalFormat.Rgba8, EnumTexturePixelFormat.Rgba, EnumTextureFilter.Nearest)
            };

            if (setupSsao)
            {
                attachments.Add(Attachment(EnumFramebufferAttachment.ColorAttachment2, width, height, EnumTextureInternalFormat.Rgba16f, EnumTexturePixelFormat.Rgba, EnumTextureFilter.Linear));
                attachments.Add(Attachment(EnumFramebufferAttachment.ColorAttachment3, width, height, EnumTextureInternalFormat.Rgba16f, EnumTexturePixelFormat.Rgba, EnumTextureFilter.Linear));

            }

            fbo = _capi.Render.CreateFrameBuffer(new FramebufferAttrs("phototesting-virtual-camera", width, height)
            {
                Attachments = attachments.ToArray()
            });

            if (setupSsao)
            {
                ApplySsaoAttachmentParameters(fbo.ColorTextureIds[2]);
                ApplySsaoAttachmentParameters(fbo.ColorTextureIds[3]);
            }

            _platform.LoadFrameBuffer((ShaderProgramBase.CurrentShaderProgram?.PassName == "gui") ? EnumFrameBuffer.Default : EnumFrameBuffer.Primary);
        }

        private static FramebufferAttrsAttachment Attachment(
            EnumFramebufferAttachment attachmentType,
            int width,
            int height,
            EnumTextureInternalFormat internalFormat,
            EnumTexturePixelFormat pixelFormat,
            EnumTextureFilter filter)
        {
            return new FramebufferAttrsAttachment
            {
                AttachmentType = attachmentType,
                Texture = new RawTexture
                {
                    Width = width,
                    Height = height,
                    PixelInternalFormat = internalFormat,
                    PixelFormat = pixelFormat,
                    MinFilter = filter,
                    MagFilter = filter
                }
            };
        }

        private static void ApplySsaoAttachmentParameters(int textureId)
        {
            GL.BindTexture(TextureTarget.Texture2D, textureId);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureBorderColor, new float[] { 1f, 1f, 1f, 1f });
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, GlClampToBorder);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, GlClampToBorder);
            GL.BindTexture(TextureTarget.Texture2D, 0);
        }

        private void SyncPerspectiveState(BlockPos frustumPos)
        {
            _main.GlMatrixModeModelView();
            _main.GlLoadMatrix(_main.MainCamera.CameraMatrix);

            double[] projectionMatrix = _capi.Render.PMatrix.Top;
            double[] modelViewMatrix = _capi.Render.MvMatrix.Top;

            for (int i = 0; i < 16; i++)
            {
                _main.PerspectiveProjectionMat[i] = projectionMatrix[i];
                _main.PerspectiveViewMat[i] = modelViewMatrix[i];
            }

            _main.frustumCuller.CalcFrustumEquations(frustumPos, projectionMatrix, modelViewMatrix);
        }

        private void RestoreMainCamera(PlayerCamera camera, DefaultShaderUniforms shUniforms, SavedMainCameraState savedState)
        {
            _capi.World.Player.Entity.CameraPos.Set(savedState.EntityCameraPos);
            camera.CamSourcePosition.Set(savedState.CamSourcePos);
            camera.OriginPosition.Set(savedState.OriginPos);
            camera.Yaw = savedState.Yaw;
            camera.Pitch = savedState.Pitch;
            camera.Roll = savedState.Roll;
            camera.Update(float.Epsilon, _main.interesectionTester);

            _main.Reset3DProjection();
            SyncPerspectiveState(savedState.EntityCameraPos.AsBlockPos);
            shUniforms.PlayerPos.Set(savedState.PlayerPos);
            shUniforms.PlayerPosForFoam.Set(savedState.PlayerPosForFoam);
            shUniforms.playerReferencePos = savedState.PlayerReferencePos?.Clone();
            shUniforms.playerReferencePosForFoam = savedState.PlayerReferencePosForFoam?.Clone();
        }

        internal void UpdateCamera()
        {
            PlayerCamera camera = _main.MainCamera;

            if (SelfPortrait)
            {
                // ThirdPerson GetCameraMatrix computes: camTarget = CamSourcePos + LocalEyePos.
                // Subtract LocalEyePos so camTarget lands exactly at our virtual eye position.
                Vec3d localEye = _capi.World.Player.Entity.LocalEyePos;
                Vec3d camSource = new Vec3d(
                    CameraPos.X - localEye.X,
                    CameraPos.Y - localEye.Y,
                    CameraPos.Z - localEye.Z);
                _capi.World.Player.Entity.CameraPos.Set(camSource);
                camera.CamSourcePosition.Set(camSource);
            }
            else
            {
                _capi.World.Player.Entity.CameraPos.Set(CameraPos);
                camera.CamSourcePosition.Set(CameraPos);
            }

            camera.OriginPosition.Set(0, 0, 0);

            camera.Yaw = this.Yaw;
            camera.Pitch = this.Pitch;
            camera.Roll = this.Roll;

            float oldFov = camera.Fov;

            if (Fov != 0) camera.Fov = Fov;

            camera.Update(float.Epsilon, _main.interesectionTester);

            DefaultShaderUniforms shUniforms = _capi.Render.ShaderUniforms;

            if (shUniforms.playerReferencePos == null)
            {
                shUniforms.playerReferencePos = new Vec3d(_main.BlockAccessor.MapSizeX / 2, 0.0, _main.BlockAccessor.MapSizeZ / 2);
            }

            if ((double)shUniforms.playerReferencePos.HorizontalSquareDistanceTo(CameraPos.X, CameraPos.Z) > 400000000.0)
            {
                shUniforms.playerReferencePos.Set((float)CameraPos.X, 0.0, (float)CameraPos.Z);
            }

            if (shUniforms.playerReferencePosForFoam == null)
            {
                shUniforms.playerReferencePosForFoam = new Vec3d(_main.BlockAccessor.MapSizeX / 2, 0.0, _main.BlockAccessor.MapSizeZ / 2);
            }

            if ((double)shUniforms.playerReferencePosForFoam.HorizontalSquareDistanceTo(CameraPos.X, CameraPos.Z) > 40000.0)
            {
                shUniforms.playerReferencePosForFoam.Set((float)CameraPos.X, 0.0, (float)CameraPos.Z);
            }

            shUniforms.PlayerPos.Set(
                (float)(CameraPos.X - shUniforms.playerReferencePos.X),
                (float)(CameraPos.Y - shUniforms.playerReferencePos.Y),
                (float)(CameraPos.Z - shUniforms.playerReferencePos.Z));

            shUniforms.PlayerPosForFoam.Set(
                (float)(CameraPos.X - shUniforms.playerReferencePosForFoam.X),
                (float)(CameraPos.Y - shUniforms.playerReferencePosForFoam.Y),
                (float)(CameraPos.Z - shUniforms.playerReferencePosForFoam.Z));

            SyncPerspectiveState(CameraPos.AsBlockPos);

            camera.Fov = oldFov;
        }

        internal void RenderCamera(float dt)
        {
            FrameBufferRef currentFbo = _platform.CurrentFrameBuffer;
            FrameBufferRef primaryFbo = _platform.FrameBuffers[0];
            FrameBufferRef transparentFbo = _platform.FrameBuffers[(int)EnumFrameBuffer.Transparent];

            // Save the main camera state that UpdateCamera() overwrites.
            PlayerCamera camera = _main.MainCamera;
            DefaultShaderUniforms shUniforms = _capi.Render.ShaderUniforms;
            SavedMainCameraState saved = new SavedMainCameraState(camera, _capi.World.Player.Entity, shUniforms);

            bool transparentDepthAttachedToVirtual = false;
            bool selfPortrait = SelfPortrait;

            // Self-portrait renders borrow the main camera object for one off-screen pass.
            int savedTppMin = camera.TppCameraDistanceMin;
            int savedTppMax = camera.TppCameraDistanceMax;
            float savedTppDist = camera.Tppcameradistance;
            EnumCameraMode savedCameraMode = GetCameraMode(camera);
            ClientPlayer? localPlayer = _capi.World.Player as ClientPlayer;
            EnumCameraMode? savedOverrideCameraMode = localPlayer?.OverrideCameraMode;
            Traverse? renderModeTraverse = selfPortrait ? TryGetRendererRenderModeField() : null;
            object? savedRenderMode = renderModeTraverse?.GetValue();

            _main.PerspectiveMode();

            try
            {
                if (selfPortrait)
                {
                    // ThirdPerson camera mode keeps animations on the body skeleton.
                    SetCameraMode(camera, EnumCameraMode.ThirdPerson);
                    if (localPlayer != null) localPlayer.OverrideCameraMode = null;
                    camera.TppCameraDistanceMin = 0;
                    camera.TppCameraDistanceMax = 0;
                    camera.Tppcameradistance = 0f;

                    if (renderModeTraverse != null)
                    {
                        object? tp = GetRenderModeThirdPerson(savedRenderMode);
                        if (tp != null) renderModeTraverse.SetValue(tp);
                    }
                }

                UpdateCamera();

                // Apply player visibility and self-portrait matrix correction before any render
                // stages run so shadow maps and the main opaque pass both see the same setup.
                _capi.World.Player.Entity.IsRendered = selfPortrait;
                VirtualCameraSelfPortraitContext.Active = selfPortrait;

                _main.Reset3DProjection();
                GL.Enable(EnableCap.DepthTest);

                // Rebuild shadow maps for the virtual camera's view frustum.
                if (_main.AmbientManager.ShadowQuality > 0)
                {
                    _main.TriggerRenderStage(EnumRenderStage.ShadowFar, dt);
                    _main.TriggerRenderStage(EnumRenderStage.ShadowFarDone, dt);
                    if (_main.AmbientManager.ShadowQuality > 1)
                    {
                        _main.TriggerRenderStage(EnumRenderStage.ShadowNear, dt);
                        _main.TriggerRenderStage(EnumRenderStage.ShadowNearDone, dt);
                    }
                }

                // Shadow stages overwrite the GL modelview matrix; re-establish the virtual camera matrices.
                SyncPerspectiveState(CameraPos.AsBlockPos);

                // ShadowDone restores GL to the screen FBO; redirect back to our virtual FBO.
                _platform.CurrentFrameBuffer = fbo;
                _platform.FrameBuffers[0] = fbo;

                // Rebuild the water-depth buffer for the virtual view.
                InvokeChunkRendererBefore(dt);

                GL.ClipPlane(ClipPlaneName.ClipDistance0, new double[] { 0d, 1d, 0d, 5d });
                GL.Enable(EnableCap.ClipDistance0);
                GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                _platform.GlDepthMask(true);
                _main.TriggerRenderStage(EnumRenderStage.Opaque, dt);

                if (_main.doTransparentRenderPass)
                {
                    // OIT/water depth attachment is hardwired to the main primary depth at startup;
                    // rebind it to our virtual depth so transparents sort against the virtual scene.
                    ScreenManager.FrameProfiler.Mark("rendTransp-begin");
                    ReattachTransparentDepth(transparentFbo, fbo.DepthTextureId);
                    transparentDepthAttachedToVirtual = true;
                    _platform.LoadFrameBuffer(EnumFrameBuffer.Transparent);
                    ScreenManager.FrameProfiler.Mark("rendTransp-fbloaded");
                    _platform.ClearFrameBuffer(EnumFrameBuffer.Transparent);
                    ScreenManager.FrameProfiler.Mark("rendTransp-bufscleared");
                    _main.TriggerRenderStage(EnumRenderStage.OIT, dt);
                    _platform.UnloadFrameBuffer(EnumFrameBuffer.Transparent);
                    ScreenManager.FrameProfiler.Mark("rendTranspDone");
                    _platform.MergeTransparentRenderPass();
                    ScreenManager.FrameProfiler.Mark("mergeTranspPassDone");
                }

                GL.Disable(EnableCap.ClipPlane0);
                _platform.GlDepthMask(true);
                _platform.GlEnableDepthTest();
                _platform.GlCullFaceBack();
                _platform.GlEnableCullFace();
                _main.TriggerRenderStage(EnumRenderStage.AfterOIT, dt);

                _platform.RenderPostprocessingEffects(_main.CurrentProjectionMatrix);
                _main.TriggerRenderStage(EnumRenderStage.AfterPostProcessing, dt);

                _platform.RenderFinalComposition();
                _main.TriggerRenderStage(EnumRenderStage.AfterFinalComposition, dt);
            }
            finally
            {
                if (transparentDepthAttachedToVirtual)
                {
                    ReattachTransparentDepth(transparentFbo, primaryFbo.DepthTextureId);
                }

                VirtualCameraSelfPortraitContext.Active = false;
                _capi.World.Player.Entity.IsRendered = saved.PlayerWasRendered;

                if (selfPortrait)
                {
                    SetCameraMode(camera, savedCameraMode);
                    if (localPlayer != null) localPlayer.OverrideCameraMode = savedOverrideCameraMode;
                    camera.TppCameraDistanceMin = savedTppMin;
                    camera.TppCameraDistanceMax = savedTppMax;
                    camera.Tppcameradistance = savedTppDist;

                    if (renderModeTraverse != null && savedRenderMode != null)
                    {
                        renderModeTraverse.SetValue(savedRenderMode);
                    }
                }

                RestoreMainCamera(camera, shUniforms, saved);

                GL.Disable(EnableCap.ClipDistance0);
                GL.Disable(EnableCap.DepthTest);
                _platform.FrameBuffers[0] = primaryFbo;
                _platform.CurrentFrameBuffer = currentFbo;
            }
        }

        private static EnumCameraMode GetCameraMode(PlayerCamera camera)
            => Traverse.Create(camera).Field<EnumCameraMode>(CameraModeFieldName).Value;

        private static void SetCameraMode(PlayerCamera camera, EnumCameraMode mode)
            => Traverse.Create(camera).Field(CameraModeFieldName).SetValue(mode);

        private Traverse? TryGetRendererRenderModeField()
        {
            object? renderer = _capi.World.Player.Entity.Properties.Client?.Renderer;
            if (renderer == null) return null;

            Traverse field = Traverse.Create(renderer).Field(RendererRenderModeFieldName);
            return field.FieldExists() ? field : null;
        }

        private void InvokeChunkRendererBefore(float dt)
        {
            Traverse chunkRenderer = Traverse.Create(_main).Field(ChunkRendererFieldName);
            if (!chunkRenderer.FieldExists()) return;

            chunkRenderer.Method(ChunkRendererBeforeMethodName, dt).GetValue();
        }

        private static void ReattachTransparentDepth(FrameBufferRef transparentFbo, int depthTextureId)
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, transparentFbo.FboId);
            GL.FramebufferTexture2D(
                FramebufferTarget.Framebuffer,
                FramebufferAttachment.DepthAttachment,
                TextureTarget.Texture2D,
                depthTextureId,
                0);
        }

        private static object? GetRenderModeThirdPerson(object? currentValue)
        {
            if (_renderModeThirdPerson != null) return _renderModeThirdPerson;

            Type? t = _renderModeType ?? currentValue?.GetType() ?? AccessTools.TypeByName("Vintagestory.GameContent.RenderMode");
            if (t == null || !t.IsEnum) return null;
            _renderModeType = t;

            try
            {
                _renderModeThirdPerson = Enum.Parse(t, "ThirdPerson");
            }
            catch
            {
                return null;
            }
            return _renderModeThirdPerson;
        }
    }
}
