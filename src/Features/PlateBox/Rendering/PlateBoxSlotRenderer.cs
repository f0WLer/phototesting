using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Phototesting.PlateLifecycle;

namespace Phototesting.PlateBox
{
    internal sealed class PlateBoxSlotRenderer : IRenderer, IDisposable
    {
        private readonly ICoreClientAPI _capi;
        private readonly BlockEntityPlateBox _owner;
        private readonly Matrixf _modelMat = new();
        private MeshRef? _slotMeshRef;   // south/north: thin in X, depth in Z  (PW × PH × PD)
        private MeshRef? _slotMeshRefEW; // east/west:   thin in Z, depth in X  (PD × PH × PW)

        // Captures the owning BE reference and client rendering API.
        public PlateBoxSlotRenderer(ICoreClientAPI capi, BlockEntityPlateBox owner)
        {
            this._capi = capi;
            this._owner = owner;
        }

        public double RenderOrder => 0.5;
        public int RenderRange => 64;

        // Draws plate-slot meshes for open plate boxes each frame on the opaque stage.
        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (stage != EnumRenderStage.Opaque) return;
            if (_owner.Api?.Side != EnumAppSide.Client) return;

            string blockPath = _owner.Block?.Code?.Path ?? string.Empty;
            bool isOpen = _owner.IsOpen || blockPath.StartsWith("platebox-open", StringComparison.OrdinalIgnoreCase);
            if (!isOpen) return;

            string facing = _owner.Block?.Variant?["facing"] ?? "south";
            bool isEW = facing == "east" || facing == "west";

            if (isEW) { if (!EnsureSlotMeshEW()) return; }
            else { if (!EnsureSlotMesh()) return; }

            MeshRef? activeMesh = isEW ? _slotMeshRefEW : _slotMeshRef;
            if (activeMesh == null || activeMesh.Disposed || !activeMesh.Initialized) return;

            EntityPlayer? player = _capi.World?.Player?.Entity;
            if (player?.CameraPos == null) return;

            string?[] slotPaths = _owner.SnapshotSlotCodePaths();
            PlateStage[] slotStages = _owner.SnapshotSlotStages();

            IStandardShaderProgram prog = _capi.Render.PreparedStandardShader(_owner.Pos.X, _owner.Pos.Y, _owner.Pos.Z);
            bool cullDisabled = false;
            try
            {
                if (_capi.BlockTextureAtlas.AtlasTextures.Count <= 0) return;

                int atlasTextureId = _capi.BlockTextureAtlas.AtlasTextures[0].TextureId;
                if (atlasTextureId == 0) return;

                prog.Tex2D = atlasTextureId;
                prog.ViewMatrix = _capi.Render.CameraMatrixOriginf;
                prog.ProjectionMatrix = _capi.Render.CurrentProjectionMatrix;

                _capi.Render.GlDisableCullFace();
                cullDisabled = true;

                for (int slotIndex = 0; slotIndex < BlockEntityPlateBox.SlotCount; slotIndex++)
                {
                    string? path = slotPaths[slotIndex];
                    if (string.IsNullOrEmpty(path)) continue;

                    prog.RgbaTint = GetSlotTint(path, slotStages[slotIndex]);

                    Vec3f origin = BlockEntityPlateBox.SlotOrigins[slotIndex];
                    float totalX = origin.X + BlockEntityPlateBox.SlotPlateOffset.X;
                    float totalZ = origin.Z + BlockEntityPlateBox.SlotPlateOffset.Z;
                    (float wx, float wz) = TransformForFacing(totalX, totalZ, facing);

                    // GetCube() returns a [-1,+1]³ cube centered at origin; Scale() doubles the value,
                    // so SlotPlateWidth etc. are half-extents. The translate point is the plate center.
                    //
                    // South/north: plate X center = totalX = origin.X + SlotPlateOffset.X (as tuned).
                    // East/west: plate Z center = TransformForFacing(totalX,...) = wz.
                    //
                    // The slot X-center (south) = (1.5+2.0)/2/16 = 1.75/16.
                    // totalX places the plate center at origin.X + 0.225/16 = 1.725/16, intentionally
                    // 0.025/16 = SlotPlateWidth*0.1 off-center toward the low wall — matching south.
                    // East needs the identical relative nudge in +Z: add SlotPlateWidth*0.1 so the
                    // plate sits in the same relative position within the rotated slot as it does in south.
                    // West: previously needed -SlotPlateWidth*0.5 to counter an over-rotation artifact.
                    float ewBaseShift = BlockEntityPlateBox.SlotPlateWidth * 0.6f;
                    float drawX = wx;
                    float drawZ = facing == "east" ? wz + ewBaseShift
                                : facing == "west" ? wz - ewBaseShift
                                : wz;

                    _modelMat.Identity()
                        .Translate(
                            (float)(_owner.Pos.X - player.CameraPos.X) + drawX,
                            (float)(_owner.Pos.Y - player.CameraPos.Y) + origin.Y + BlockEntityPlateBox.SlotPlateOffset.Y + BlockEntityPlateBox.SlotPlateTopAlignYOffset,
                            (float)(_owner.Pos.Z - player.CameraPos.Z) + drawZ
                        );

                    prog.ModelMatrix = _modelMat.Values;
                    _capi.Render.RenderMesh(activeMesh);
                }
            }
            finally
            {
                if (cullDisabled)
                {
                    _capi.Render.GlEnableCullFace();
                }

                prog.Stop();
            }
        }

        // Builds or reuses the south/north-oriented slot mesh.
        private bool EnsureSlotMesh()
        {
            if (_slotMeshRef != null && !_slotMeshRef.Disposed && _slotMeshRef.Initialized)
            {
                return true;
            }

            _slotMeshRef?.Dispose();
            _slotMeshRef = null;

            try
            {
                ITexPositionSource source = _capi.Tesselator.GetTextureSource(_owner.Block);
                TextureAtlasPosition? texPos = source["plate"];
                if (texPos == null || texPos == _capi.BlockTextureAtlas.UnknownTexturePosition) return false;

                MeshData mesh = CubeMeshUtil.GetCube();
                mesh = mesh.WithTexPos(texPos);
                mesh.Scale(new Vec3f(0f, 0f, 0f), BlockEntityPlateBox.SlotPlateWidth, BlockEntityPlateBox.SlotPlateRenderHeight, BlockEntityPlateBox.SlotPlateDepth);
                mesh.Rgba?.Fill((byte)255);

                _slotMeshRef = _capi.Render.UploadMesh(mesh);
                return _slotMeshRef != null;
            }
            catch
            {
                return false;
            }
        }

        // Builds or reuses the east/west-oriented slot mesh.
        private bool EnsureSlotMeshEW()
        {
            if (_slotMeshRefEW != null && !_slotMeshRefEW.Disposed && _slotMeshRefEW.Initialized)
            {
                return true;
            }

            _slotMeshRefEW?.Dispose();
            _slotMeshRefEW = null;

            try
            {
                ITexPositionSource source = _capi.Tesselator.GetTextureSource(_owner.Block);
                TextureAtlasPosition? texPos = source["plate"];
                if (texPos == null || texPos == _capi.BlockTextureAtlas.UnknownTexturePosition) return false;

                MeshData mesh = CubeMeshUtil.GetCube();
                mesh = mesh.WithTexPos(texPos);
                // Swap X and Z so the plate stands thin in Z and has depth in X — correct for E/W boxes.
                mesh.Scale(new Vec3f(0f, 0f, 0f), BlockEntityPlateBox.SlotPlateDepth, BlockEntityPlateBox.SlotPlateRenderHeight, BlockEntityPlateBox.SlotPlateWidth);
                mesh.Rgba?.Fill((byte)255);

                _slotMeshRefEW = _capi.Render.UploadMesh(mesh);
                return _slotMeshRefEW != null;
            }
            catch
            {
                return false;
            }
        }

        // Chooses a stage-aware tint fallback for slot plates.
        private static Vec4f GetSlotTint(string path, PlateStage stage)
        {
            if (stage == PlateStage.Rough) return new Vec4f(0.80f, 0.82f, 0.86f, 1f);
            if (stage == PlateStage.Clean) return new Vec4f(0.94f, 0.96f, 0.98f, 1f);
            if (stage == PlateStage.Sensitizing) return new Vec4f(0.88f, 0.86f, 0.84f, 1f);
            if (stage == PlateStage.Sensitized) return new Vec4f(0.96f, 0.96f, 0.98f, 1f);
            if (stage == PlateStage.Exposed) return new Vec4f(0.80f, 0.76f, 0.72f, 1f);
            if (stage == PlateStage.Developing || stage == PlateStage.Developed) return new Vec4f(0.64f, 0.59f, 0.55f, 1f);
            if (stage == PlateStage.Finished) return new Vec4f(0.47f, 0.45f, 0.43f, 1f);

            if (path.Contains("sensitizedplate", StringComparison.OrdinalIgnoreCase)) return new Vec4f(0.96f, 0.96f, 0.98f, 1f);
            if (path.Contains("photoplate", StringComparison.OrdinalIgnoreCase)) return new Vec4f(0.64f, 0.59f, 0.55f, 1f);
            if (path.Contains("phototestingcoatedplate", StringComparison.OrdinalIgnoreCase)) return new Vec4f(0.88f, 0.86f, 0.84f, 1f);
            if (path.Contains("cleanglassplate", StringComparison.OrdinalIgnoreCase)) return new Vec4f(0.94f, 0.96f, 0.98f, 1f);
            if (path.Contains("roughglassplate", StringComparison.OrdinalIgnoreCase)) return new Vec4f(0.80f, 0.82f, 0.86f, 1f);
            return new Vec4f(0.92f, 0.92f, 0.92f, 1f);
        }

        // Releases uploaded mesh references when the renderer is torn down.
        public void Dispose()
        {
            _slotMeshRef?.Dispose();
            _slotMeshRef = null;
            _slotMeshRefEW?.Dispose();
            _slotMeshRefEW = null;
        }

        /// <summary>Forward-rotate an XZ slot position from south-model space to world space for the given facing.
        /// Matches VS Mat4f.RotateY convention (positive = CW viewed from above).
        /// 90°CW:  (x,z) → (1-z, x) = (cx-dz, cx+dx)
        /// 180°:   (x,z) → (1-x, 1-z) = (cx-dx, cx-dz)
        /// 270°CW: (x,z) → (z, 1-x)  = (cx+dz, cx-dx)
        /// </summary>
        private static (float, float) TransformForFacing(float x, float z, string facing)
        {
            float cx = 0.5f;
            float dx = x - cx, dz = z - cx;
            return facing switch
            {
                "east" => (cx - dz, cx + dx),  // 90°CW:  (1-z, x)
                "north" => (cx + dx, cx - dz),  // 180°+X-flip: (x, 1-z)
                "west" => (cx + dz, cx - dx),  // 270°CW: (z, 1-x)
                _ => (cx - dx, cx + dz)   // south X-flip: (1-x, z)
            };
        }
    }
}
