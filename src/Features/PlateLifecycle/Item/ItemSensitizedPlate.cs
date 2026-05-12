using Vintagestory.API.Common;

namespace Phototesting.PlateLifecycle
{
    public sealed class ItemSensitizedPlate : ItemPlateBase
    {
        public override void OnModifiedInInventorySlot(IWorldAccessor world, ItemSlot slot, ItemStack? extractedStack = null)
        {
            base.OnModifiedInInventorySlot(world, slot, extractedStack);
            if (world?.Side != EnumAppSide.Server) return;
            if (slot?.Itemstack == null) return;

            if (PlateStateService.GetStage(slot.Itemstack) == PlateStage.Finished) return;

            double duration = WetPlateAttrs.ResolveWetDurationHours(api);
            WetPlateAttrs.EnsureWetTimer(world, slot.Itemstack, duration);
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, System.Text.StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

            ItemStack? stack = inSlot?.Itemstack;
            if (stack == null) return;

            WetPlateAttrs.AppendWetnessInfo(world, stack, dsc);
        }
    }
}
