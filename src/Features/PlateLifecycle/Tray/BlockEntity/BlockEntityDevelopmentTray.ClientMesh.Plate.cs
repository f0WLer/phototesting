using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Phototesting.PlateLifecycle.Rendering;

namespace Phototesting.PlateLifecycle.Tray
{
    public sealed partial class BlockEntityDevelopmentTray
    {
        // Builds either the photo-mapped plate mesh or the plain plate mesh depending on the inserted plate stage.
        private bool TryBuildPlateMesh(ICoreClientAPI capi, ItemStack plate, out MeshData? mesh)
        {
            mesh = null;
            bool showPhoto = ShouldShowTrayPhoto(plate);

            if (showPhoto && PhotoPlateRenderUtil.TryGetPhotoBlockTexture(capi, plate, out TextureAtlasPosition photoTex, out float photoAspect, Pos))
            {
                try
                {
                    ITexPositionSource baseSource = capi.Tesselator.GetTextureSource(Block);
                    ITexPositionSource texSource = new PlatePhotoTextureSource(baseSource, photoTex);

                    var shape = Block?.Shape?.Clone();
                    if (shape == null) return false;

                    shape.IgnoreElements = new[] { "base", "wall-n", "wall-s", "wall-e", "wall-w" };
                    capi.Tesselator.TesselateShape(
                        "phototesting-devtray-platephoto",
                        Block?.Code ?? new AssetLocation("phototesting", "developmenttray-red"),
                        shape,
                        out mesh,
                        texSource
                    );

                    PhotoMeshUtil.StampUvByRotationCropped(mesh, photoTex, 0, photoAspect, PhotoMeshUtil.PhotoTargetAspect);

                    int placementYawDeg = GetPlacementFacingYawDeg();
                    if (placementYawDeg != 0)
                        mesh.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0f, placementYawDeg * GameMath.DEG2RAD, 0f);

                    mesh.Translate(0f, 0.0006f, 0f);
                    return true;
                }
                catch
                {
                    mesh = null;
                    return false;
                }
            }

            if (!showPhoto)
            {
                try
                {
                    ITexPositionSource baseSource = capi.Tesselator.GetTextureSource(Block);
                    var shape = Block?.Shape?.Clone();
                    if (shape == null) return false;

                    shape.IgnoreElements = new[] { "base", "wall-n", "wall-s", "wall-e", "wall-w" };
                    capi.Tesselator.TesselateShape(
                        "phototesting-devtray-plainplate",
                        Block?.Code ?? new AssetLocation("phototesting", "developmenttray-red"),
                        shape,
                        out mesh,
                        baseSource
                    );

                    int placementYawDeg = GetPlacementFacingYawDeg();
                    if (placementYawDeg != 0)
                        mesh.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0f, placementYawDeg * GameMath.DEG2RAD, 0f);

                    mesh.Translate(0f, 0.0006f, 0f);
                    return mesh != null;
                }
                catch
                {
                    mesh = null;
                    return false;
                }
            }

            return false;
        }

        // Limits the tray photo overlay to the stages where the developed image should already be visible.
        private static bool ShouldShowTrayPhoto(ItemStack plate)
        {
            if (plate?.Attributes == null) return false;

            PlateStage stage = PlateStateService.GetStage(plate);
            if (stage == PlateStage.Developing) return true;
            if (stage == PlateStage.Developed) return true;
            if (stage == PlateStage.Finished) return true;

            return false;
        }

        // Converts stored tray placement facing into the yaw used for rotating client meshes.
        private int GetPlacementFacingYawDeg()
        {
            string facing = PlacementFacingCode;
            return facing switch
            {
                "south" => 90,
                "west" => 0,
                "north" => 270,
                _ => 180
            };
        }
    }
}
