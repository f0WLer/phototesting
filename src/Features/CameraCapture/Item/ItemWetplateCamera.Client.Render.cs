using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Phototesting.CameraCapture
{
    // Camera render-time overrides and ground-mesh cache handling.
    // Keeps rendering concerns separate from interaction and exposure logic.
    public partial class ItemWetplateCamera
    {
        private static readonly object _groundMeshLock = new object();
        private static readonly Vec3f _groundMeshScaleCenter = new Vec3f(0.5f, 0.5f, 0.5f);
        private static MultiTextureMeshRef? _groundMeshRef;

        // Builds and caches the enlarged ground mesh.
        private bool TryGetGroundMesh(ICoreClientAPI capi, out MultiTextureMeshRef? meshRef)
        {
            lock (_groundMeshLock)
            {
                if (_groundMeshRef != null)
                {
                    meshRef = _groundMeshRef;
                    return true;
                }
            }

            try
            {
                capi.Tesselator.TesselateItem(this, out MeshData mesh);

                // Scale around center
                mesh.Scale(_groundMeshScaleCenter, 2.5f, 2.5f, 2.5f);

                var meshRefLocal = capi.Render.UploadMultiTextureMesh(mesh);

                lock (_groundMeshLock)
                {
                    if (_groundMeshRef != null)
                    {
                        meshRef = _groundMeshRef;
                        meshRefLocal.Dispose();
                        return true;
                    }

                    _groundMeshRef = meshRefLocal;
                }

                meshRef = meshRefLocal;
                return true;
            }
            catch
            {
                meshRef = null;
                return false;
            }
        }

        // Applies ground-mesh and GUI transform overrides for client camera rendering.
        public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
        {
#pragma warning disable CS0618 // Preserve FP pose behavior on older targets
            base.OnBeforeRender(capi, itemstack, target, ref renderinfo);

            if (target == EnumItemRenderTarget.Ground)
            {
                if (TryGetGroundMesh(capi, out var meshRef) && meshRef != null)
                {
                    renderinfo.ModelRef = meshRef;
                    return;
                }
            }
#pragma warning restore CS0618

            if (target != EnumItemRenderTarget.Gui) return;

            // Preserve JSON guiTransform values while keeping GUI preview stationary.
            var transform = renderinfo.Transform == null
                ? new ModelTransform()
                : CloneTransform(renderinfo.Transform);

            if (renderinfo.Transform == null)
            {
                transform.Translation = new FastVec3f(0f, 0f, 0f);
                transform.Rotation = new FastVec3f(0f, 0f, 0f);
                transform.Origin.X = 0.5f;
                transform.Origin.Y = 0.5f;
                transform.Origin.Z = 0.5f;
                transform.ScaleXYZ = new FastVec3f(1f, 1f, 1f);
            }

            transform.Rotate = false;
            renderinfo.Transform = transform;
        }

        // Copy transform data so render changes never mutate shared engine-owned instances.
        private static ModelTransform CloneTransform(ModelTransform src)
        {
            var dst = new ModelTransform();

            dst.Translation.X = src.Translation.X;
            dst.Translation.Y = src.Translation.Y;
            dst.Translation.Z = src.Translation.Z;

            dst.Rotation.X = src.Rotation.X;
            dst.Rotation.Y = src.Rotation.Y;
            dst.Rotation.Z = src.Rotation.Z;

            dst.Origin.X = src.Origin.X;
            dst.Origin.Y = src.Origin.Y;
            dst.Origin.Z = src.Origin.Z;

            dst.ScaleXYZ = src.ScaleXYZ;
            dst.Rotate = src.Rotate;
            return dst;
        }

        // Releases the cached ground mesh when the item is unloaded on the client.
        public override void OnUnloaded(ICoreAPI api)
        {
            base.OnUnloaded(api);

            if (api.Side != EnumAppSide.Client) return;

            lock (_groundMeshLock)
            {
                _groundMeshRef?.Dispose();
                _groundMeshRef = null;
            }
        }
    }
}
