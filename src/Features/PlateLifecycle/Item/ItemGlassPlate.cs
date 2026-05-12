using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Phototesting.PlateLifecycle.Integration;

namespace Phototesting.PlateLifecycle
{
    /// <summary>
    /// Glass plate item that places the matching plate block on the ground.
    /// Sensitization chemistry is handled by ground-block interactions in <see cref="BlockGlassPlate"/>.
    /// </summary>
    public sealed partial class ItemGlassPlate : ItemPlateBase
    {
        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            if (api.World == null || slot?.Itemstack == null) return;
            if (blockSel == null) return;
            if (blockSel.Face != BlockFacing.UP) return;

            handling = EnumHandHandling.PreventDefault;

            if (api.Side != EnumAppSide.Server) return;

            string defaultPlateBlockState = Attributes?["plateBlockState"].AsString("rough") ?? "rough";
            GlassPlatePlacementIntegration.HandlePlacement(api, slot, blockSel, defaultPlateBlockState);
        }
    }
}