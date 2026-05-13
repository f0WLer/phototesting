using Vintagestory.API.Common;

namespace Phototesting.PlateLifecycle
{
    public sealed class ItemSensitizedPlate : ItemPlateBase
    {
        public override void OnModifiedInInventorySlot(IWorldAccessor world, ItemSlot slot, ItemStack? extractedStack = null)
        {
            base.OnModifiedInInventorySlot(world, slot, extractedStack);
            // Drying state is initialized by PlateLifecycleStateCoordinator on sensitization
            // and ticked by the vanilla transition pipeline; nothing to do here.
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
