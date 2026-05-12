using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Phototesting.PlateLifecycle.Tray
{
    public sealed partial class BlockEntityDevelopmentTray
    {
        // Tesselates the tray body without the plate element so the custom plate/photo mesh can be layered separately.
        private bool TryBuildTrayBodyMesh(ICoreClientAPI capi, out MeshData? mesh)
        {
            mesh = null;
            try
            {
                ITexPositionSource bodySource = capi.Tesselator.GetTextureSource(Block);
                var bodyShape = Block?.Shape?.Clone();
                if (bodyShape == null) return false;

                bodyShape.IgnoreElements = new[] { "plate" };
                capi.Tesselator.TesselateShape(
                    "phototesting-devtray-body",
                    Block?.Code ?? new AssetLocation("phototesting", "developmenttray-red"),
                    bodyShape,
                    out mesh,
                    bodySource
                );
                return mesh != null;
            }
            catch
            {
                mesh = null;
                return false;
            }
        }
    }
}
