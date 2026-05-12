using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Phototesting.PlateBox
{
    public sealed partial class BlockPlateBox
    {
        // Handles shift-pickup inventory transfer and authoritative block removal.
        private bool TryPickupBoxAndGiveDrop(IWorldAccessor world, IPlayer byPlayer, BlockPos pos)
        {
            if (world == null || byPlayer == null || pos == null) return false;

            ItemStack? stack = CreateDropStack(world, pos);
            if (stack != null)
            {
                TryGiveOrSpawnStack(world, byPlayer, pos, stack);
            }

            world.BlockAccessor.SetBlock(0, pos);
            world.BlockAccessor.RemoveBlockEntity(pos);
            return true;
        }

        // Gives a stack to player inventory first and falls back to spawning in-world when full.
        private static bool TryGiveOrSpawnStack(IWorldAccessor world, IPlayer byPlayer, BlockPos pos, ItemStack stack)
        {
            if (world == null || byPlayer == null || pos == null || stack == null) return false;

            bool given = byPlayer.InventoryManager?.TryGiveItemstack(stack) ?? false;
            if (given) return true;

            world.SpawnItemEntity(stack, pos.ToVec3d().Add(0.5, 0.5, 0.5));
            return true;
        }

        // Swaps between open/closed facing variants while preserving BlockEntity slot state.
        private static bool TrySetOpenState(IWorldAccessor world, BlockPos pos, BlockEntityPlateBox be, bool open)
        {
            if (world == null || pos == null || be == null) return false;

            string facing = world.BlockAccessor.GetBlock(pos)?.Variant?["facing"] ?? "south";
            AssetLocation targetCode = open
                ? new AssetLocation("phototesting", "platebox-open-" + facing)
                : new AssetLocation("phototesting", "platebox-" + facing);
            Block? target = world.GetBlock(targetCode);
            if (target == null)
            {
                return be.SetOpen(open) || be.IsOpen == open;
            }

            if (world.BlockAccessor.GetBlock(pos)?.Code == targetCode)
            {
                bool changedSameBlock = be.SetOpen(open) || be.IsOpen == open;
                if (changedSameBlock)
                {
                    PlayOpenCloseSoundPair(world, pos);
                }

                return changedSameBlock;
            }

            var snapshot = new TreeAttribute();
            be.SetOpen(open);
            be.ToTreeAttributes(snapshot);

            world.BlockAccessor.SetBlock(target.Id, pos);

            if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityPlateBox newBe)
            {
                newBe.FromTreeAttributes(snapshot, world);
                newBe.MarkDirty(true);
                try { world.BlockAccessor.MarkBlockEntityDirty(pos); }
                catch (Exception ex) { Log.Debug(world.Logger, "ToggleOpenClose: MarkBlockEntityDirty failed: {0}", ex.Message); }
                try { world.BlockAccessor.MarkBlockDirty(pos); }
                catch (Exception ex) { Log.Debug(world.Logger, "ToggleOpenClose: MarkBlockDirty failed: {0}", ex.Message); }
                PlayOpenCloseSoundPair(world, pos);
                return true;
            }

            return false;
        }

        // Plays the plate-box open/close foley sequence on the authoritative server side.
        private static void PlayOpenCloseSoundPair(IWorldAccessor world, BlockPos pos)
        {
            if (world == null || pos == null || world.Side != EnumAppSide.Server) return;

            double x = pos.X + 0.5;
            double y = pos.Y + 0.5;
            double z = pos.Z + 0.5;

            PlaySoundWithDelay(world, x, y, z, _padlockSound, 0);
            PlaySoundWithDelay(world, x, y, z, _padlockSound, OpenCloseSoundDelayMs);
            PlaySoundWithDelay(world, x, y, z, _hingeSound, OpenCloseSoundDelayMs * 2);
        }

        // Schedules delayed sounds with an immediate fallback when callback scheduling fails.
        private static void PlaySoundWithDelay(IWorldAccessor world, double x, double y, double z, AssetLocation sound, int delayMs)
        {
            if (delayMs <= 0)
            {
                world.PlaySoundAt(sound, x, y, z, null, true, 16f, 1f);
                return;
            }

            try
            {
                world.Api?.Event?.RegisterCallback(_ =>
                {
                    try
                    {
                        if (world.Side != EnumAppSide.Server) return;
                        world.PlaySoundAt(sound, x, y, z, null, true, 16f, 1f);
                    }
                    catch (Exception ex) { Log.Debug(world.Logger, "PlaySoundWithDelay callback failed: {0}", ex.Message); }
                }, delayMs);
            }
            catch (Exception ex)
            {
                Log.Warn(world.Logger, "PlaySoundWithDelay scheduling failed, using immediate fallback: {0}", ex.Message);
                world.PlaySoundAt(sound, x, y, z, null, true, 16f, 1f);
            }
        }
    }
}
