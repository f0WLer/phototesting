using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Phototesting.PlateLifecycle.Blocks
{
    public sealed partial class BlockGlassPlate
    {
        // Completes rough-plate polishing and optionally consumes plain cloth according to config.
        private bool HandlePolishInteraction(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockPos pos)
        {
            if (!IsPolishModifierDown(byPlayer) || !IsHoldingPlainCloth(byPlayer)) return false;
            if (secondsUsed < GetPolishSeconds()) return true;
            if (world.Side != EnumAppSide.Server) return false;

            Block? cleanBlock = GetBlockForState(world, "clean");
            if (cleanBlock == null) return false;

            bool isCreative = byPlayer.WorldData?.CurrentGameMode == EnumGameMode.Creative;
            int consumeCount = GetPlainClothConsumeCount();
            ItemSlot? activeSlot = byPlayer.InventoryManager?.ActiveHotbarSlot;
            if (!isCreative && consumeCount > 0)
            {
                if (activeSlot?.Itemstack == null || activeSlot.Itemstack.StackSize < consumeCount)
                {
                    return false;
                }
            }

            world.BlockAccessor.SetBlock(cleanBlock.Id, pos);
            world.BlockAccessor.MarkBlockDirty(pos);

            if (!isCreative && consumeCount > 0)
            {
                activeSlot!.TakeOut(consumeCount);
                activeSlot.MarkDirty();
            }

            return false;
        }

        // Builds the polish-specific held-help prompt shown for rough plates.
        private WorldInteraction[] BuildPolishInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
        {
            Item? clothItem = world.GetItem(_plainClothCode);
            if (clothItem == null) return base.GetPlacedBlockInteractionHelp(world, selection, forPlayer);

            return new[]
            {
                new WorldInteraction
                {
                    ActionLangCode = "phototesting:heldhelp-cleanroughglass",
                    HotKeyCode = "sneak",
                    MouseButton = EnumMouseButton.Right,
                    Itemstacks = new[] { new ItemStack(clothItem) }
                }
            };
        }

        // Checks whether the active hotbar slot holds the plain cloth item used for polishing.
        private static bool IsHoldingPlainCloth(IPlayer player)
        {
            ItemSlot? activeSlot = player.InventoryManager?.ActiveHotbarSlot;
            ItemStack? held = activeSlot?.Itemstack;
            return held?.Collectible?.Code != null && held.Collectible.Code == _plainClothCode;
        }

        // Uses sneak as the polish modifier so polishing does not collide with normal pickup behavior.
        private static bool IsPolishModifierDown(IPlayer player)
        {
            var controls = player.Entity?.Controls;
            return controls?.ShiftKey == true || controls?.Sneak == true;
        }

        // Treats an empty active slot as the pickup gesture for placed plates.
        private static bool IsEmptyHand(IPlayer player)
        {
            ItemSlot? activeSlot = player.InventoryManager?.ActiveHotbarSlot;
            return activeSlot?.Itemstack == null;
        }

        // Gives the matching plate item to the player and removes the placed plate block from the world.
        private void GiveItemAndRemoveBlock(IWorldAccessor world, IPlayer byPlayer, BlockPos pos)
        {
            if (byPlayer is IServerPlayer sp && TryCreatePlateItemStack(world, pos, out ItemStack stack))
            {
                if (!sp.InventoryManager.TryGiveItemstack(stack))
                {
                    world.SpawnItemEntity(stack, pos.ToVec3d().Add(0.5, 0.5, 0.5));
                }
            }

            world.BlockAccessor.SetBlock(0, pos);
            world.BlockAccessor.MarkBlockDirty(pos);
        }
    }
}

