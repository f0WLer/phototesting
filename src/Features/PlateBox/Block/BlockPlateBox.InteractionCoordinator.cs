using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Phototesting.PlateBox
{
    public sealed partial class BlockPlateBox
    {
        // Keeps engine callback ownership local and delegates heavy interaction branching.
        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (world == null || byPlayer == null || blockSel?.Position == null) return false;
            if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BlockEntityPlateBox be) return false;
            return HandlePlateBoxInteractionStart(world, byPlayer, blockSel, be);
        }

        // Routes shift-pickup, open/close toggles, and per-slot insert/remove interactions.
        private bool HandlePlateBoxInteractionStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, BlockEntityPlateBox be)
        {
            if (IsShiftDown(byPlayer))
            {
                if (world.Side == EnumAppSide.Client) return true;
                return TryPickupBoxAndGiveDrop(world, byPlayer, blockSel.Position);
            }

            ItemSlot? activeSlot = byPlayer.InventoryManager?.ActiveHotbarSlot;
            ItemStack? held = activeSlot?.Itemstack;

            if (!be.IsOpen)
            {
                if (world.Side == EnumAppSide.Client) return true;
                return TrySetOpenState(world, blockSel.Position, be, open: true);
            }

            string blockFacing = world.BlockAccessor.GetBlock(blockSel.Position)?.Variant?["facing"] ?? "south";
            int slotIndex = GetSlotIndexFromHit(blockSel, blockFacing);
            bool clickedSlot = slotIndex >= 0;

            if (!clickedSlot)
            {
                if (world.Side == EnumAppSide.Client) return true;
                return TrySetOpenState(world, blockSel.Position, be, open: false);
            }

            if (held != null && BlockEntityPlateBox.IsInsertablePlate(held))
            {
                // Keep slot clicks owned by the box even when insert cannot proceed.
                if (world.Side == EnumAppSide.Client)
                {
                    return true;
                }

                if (activeSlot == null) return true;
                if (!be.TryInsertPlateAt(slotIndex, held, world)) return true;

                activeSlot.TakeOut(1);
                activeSlot.MarkDirty();
                PlayRandomPlateSetSound(world, blockSel.Position, byPlayer);
                return true;
            }

            if (held == null && !be.HasPlateAt(slotIndex))
            {
                return false;
            }

            if (held == null)
            {
                if (world.Side == EnumAppSide.Client)
                {
                    return be.HasPlateAt(slotIndex);
                }

                ItemStack? taken = be.TakePlateAt(slotIndex, world);
                if (taken == null) return false;

                if (activeSlot != null && activeSlot.Itemstack == null)
                {
                    activeSlot.Itemstack = taken;
                    activeSlot.MarkDirty();
                    return true;
                }

                TryGiveOrSpawnStack(world, byPlayer, blockSel.Position, taken);
                PlayRandomPlateSetSound(world, blockSel.Position, byPlayer);

                return true;
            }

            return false;
        }

        // Maps a block-selection hit point to the corresponding logical slot index.
        private static int GetSlotIndexFromHit(BlockSelection blockSel, string facing)
        {
            if (blockSel?.HitPosition == null) return -1;

            double hitX = blockSel.HitPosition.X;
            double hitY = blockSel.HitPosition.Y;
            double hitZ = blockSel.HitPosition.Z;

            (hitX, hitZ) = InverseFacingTransform(hitX, hitZ, facing);

            const double pad = 0.01;

            for (int index = 0; index < _slotHitBoxes.Length; index++)
            {
                Cuboidf box = _slotHitBoxes[index];

                if (hitX < box.X1 - pad || hitX > box.X2 + pad) continue;
                if (hitY < box.Y1 - pad || hitY > box.Y2 + pad) continue;
                if (hitZ < box.Z1 - pad || hitZ > box.Z2 + pad) continue;

                return index;
            }

            return -1;
        }

        // Converts hit coordinates into south-facing model space for slot hitbox checks.
        private static (double, double) InverseFacingTransform(double x, double z, string facing)
        {
            return facing switch
            {
                "east" => (z, 1.0 - x),
                "north" => (x, 1.0 - z),
                "west" => (1.0 - z, x),
                _ => (1.0 - x, z)
            };
        }

        // Normalizes both Shift and Sneak into one interaction modifier check.
        private static bool IsShiftDown(IPlayer player)
        {
            var controls = player?.Entity?.Controls;
            return controls?.ShiftKey == true || controls?.Sneak == true;
        }

        // Plays a randomized glass set/remove sound to avoid repetitive slot foley.
        private static void PlayRandomPlateSetSound(IWorldAccessor world, BlockPos pos, IPlayer? byPlayer)
        {
            if (world == null || pos == null || world.Side != EnumAppSide.Server) return;

            int index = 0;
            float pitch = 1f;

            try
            {
                if (world.Rand != null)
                {
                    index = world.Rand.Next(_plateSetSounds.Length);
                    pitch = 0.92f + (float)world.Rand.NextDouble() * 0.16f;
                }
            }
            catch
            {
                index = 0;
                pitch = 1f;
            }

            AssetLocation sound = _plateSetSounds[index];
            world.PlaySoundAt(sound, pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5, null, true, 16f, pitch);
        }
    }
}
