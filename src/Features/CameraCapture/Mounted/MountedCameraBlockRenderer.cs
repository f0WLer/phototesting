using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Phototesting.CameraCapture
{
    // Renders the mounted camera block entity mesh during EnumRenderStage.Opaque using the
    // standard block shader.  Unlike chunk-tessellated geometry this renderer can check
    // VirtualCameraRenderContext.IsVirtualRender at frame time and skip, ensuring the
    // camera block never appears in the virtual capture output.
    internal sealed class MountedCameraBlockRenderer : IRenderer
    {
        private readonly ICoreClientAPI _capi;
        private readonly BlockPos _pos;
        private readonly Block _block;
        private float _facingYaw;
        private MultiTextureMeshRef? _meshRef;
        private bool _meshDirty = true;
        private bool _disposed;

        private readonly Matrixf _modelMat = new Matrixf();

        public double RenderOrder => 0.4;
        public int RenderRange => 0; // unlimited — chunk presence already constrains load distance

        internal MountedCameraBlockRenderer(ICoreClientAPI capi, BlockPos pos, Block block, float facingYaw)
        {
            _capi = capi;
            _pos = pos.Copy();
            _block = block;
            _facingYaw = facingYaw;
        }

        internal void SetFacingYaw(float yaw)
        {
            _facingYaw = yaw;
            _meshDirty = true;
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (_disposed || VirtualCameraRenderContext.IsVirtualRender) return;

            if (_meshDirty) RebuildMesh();
            if (_meshRef == null) return;

            Vec3d camPos = _capi.World.Player.Entity.CameraPos;
            Vec4f light = _capi.World.BlockAccessor.GetLightRGBs(_pos.X, _pos.Y, _pos.Z);

            IStandardShaderProgram prog = _capi.Render.PreparedStandardShader(_pos.X, _pos.Y, _pos.Z);
            prog.ViewMatrix = _capi.Render.CameraMatrixOriginf;
            prog.ProjectionMatrix = _capi.Render.CurrentProjectionMatrix;
            prog.RgbaLightIn = light;
            prog.ModelMatrix = _modelMat.Identity()
                .Translate(_pos.X - camPos.X, _pos.Y - camPos.Y, _pos.Z - camPos.Z)
                .Values;

            _capi.Render.RenderMultiTextureMesh(_meshRef, "tex");
            ((IShaderProgram)prog).Stop();
        }

        private void RebuildMesh()
        {
            _meshRef?.Dispose();
            _meshRef = null;
            _meshDirty = false;

            try
            {
                _capi.Tesselator.TesselateBlock(_block, out MeshData mesh);
                mesh.Rotate(0f, _facingYaw, 0f);
                _meshRef = _capi.Render.UploadMultiTextureMesh(mesh);
            }
            catch (Exception ex)
            {
                _capi.Logger.Warning("Phototesting: MountedCameraBlockRenderer mesh build failed at {0}: {1}", _pos, ex.Message);
            }
        }

        public void Dispose()
        {
            _disposed = true;
            _meshRef?.Dispose();
            _meshRef = null;
        }
    }
}
