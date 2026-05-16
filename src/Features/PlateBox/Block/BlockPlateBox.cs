using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Phototesting.PlateBox
{
    // Plate-box block identity, placement, drop serialization, and partial-family routing.
    // Starts from block lifecycle callbacks and delegates rendering/help and slot interaction to sibling partials.
    // Delegates persistent slot state to BlockEntityPlateBox and presentation/help to sibling partials.
    // Side: mixed block owner. Keep authoritative slot mutation in the interaction partial, not in render/presentation helpers.
    // Related files: BlockPlateBox.Interaction.cs, BlockPlateBox.Presentation.cs, BlockEntityPlateBox.cs.
    public sealed partial class BlockPlateBox : Block
    {
        private static readonly AssetLocation _samplePlateCode = new("phototesting", "sensitizedplate");
        private static readonly AssetLocation _closedBoxCode = new("phototesting", "platebox-north");
        private static readonly Cuboidf[] _slotHitBoxes =
        {
            // Matches platehb1..platehb8 in assets/phototesting/shapes/block/platebox-open.json
            new(1.5f / 16f, 0.5f / 16f, 4.5f / 16f, 2.0f / 16f, 8.2f / 16f, 11.5f / 16f),
            new(3.0f / 16f, 0.5f / 16f, 4.5f / 16f, 3.5f / 16f, 8.2f / 16f, 11.5f / 16f),
            new(4.5f / 16f, 0.5f / 16f, 4.5f / 16f, 5.0f / 16f, 8.2f / 16f, 11.5f / 16f),
            new(6.0f / 16f, 0.5f / 16f, 4.5f / 16f, 6.5f / 16f, 8.2f / 16f, 11.5f / 16f),
            new(9.5f / 16f, 0.5f / 16f, 4.5f / 16f, 10.0f / 16f, 8.2f / 16f, 11.5f / 16f),
            new(11.0f / 16f, 0.5f / 16f, 4.5f / 16f, 11.5f / 16f, 8.2f / 16f, 11.5f / 16f),
            new(12.5f / 16f, 0.5f / 16f, 4.5f / 16f, 13.0f / 16f, 8.2f / 16f, 11.5f / 16f),
            new(14.0f / 16f, 0.5f / 16f, 4.5f / 16f, 14.5f / 16f, 8.2f / 16f, 11.5f / 16f)
        };
        private static readonly AssetLocation _padlockSound = new("game", "sounds/tool/padlock");
        private static readonly AssetLocation _hingeSound = new("phototesting", "sounds/hinge");
        private static readonly AssetLocation _woodThudSound = new("phototesting", "sounds/wood-thud");
        private const int OpenCloseSoundDelayMs = 35;
        private static readonly AssetLocation[] _plateSetSounds =
        {
            new("phototesting", "sounds/glass-set1"),
            new("phototesting", "sounds/glass-set2"),
            new("phototesting", "sounds/glass-set3")
        };

        // Hides rotated variants in creative tabs so only the canonical north item appears.
        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            PartialSelection = false;

            string facing = Variant?["facing"] ?? string.Empty;
            if (!facing.Equals("north", StringComparison.OrdinalIgnoreCase))
            {
                CreativeInventoryTabs = Array.Empty<string>();
                CreativeInventoryStacks = Array.Empty<CreativeTabAndStackList>();
            }
        }

        // Drops a serialized closed-box stack so stored plates persist through breakage.
        public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f)
        {
            ItemStack? stack = CreateDropStack(world, pos);
            if (stack != null)
            {
                return new[] { stack };
            }

            return base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier);
        }

        // Returns a serialized closed-box stack for creative pick-block.
        public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
        {
            ItemStack? stack = CreateDropStack(world, pos);
            if (stack != null) return stack;
            return base.OnPickBlock(world, pos);
        }

        // Builds the drop stack and copies BlockEntity slot data into item attributes.
        private ItemStack? CreateDropStack(IWorldAccessor world, BlockPos pos)
        {
            Block? closedBlock = world?.GetBlock(_closedBoxCode);
            ItemStack stack = closedBlock != null ? new ItemStack(closedBlock) : new ItemStack(this);
            if (world?.BlockAccessor?.GetBlockEntity(pos) is BlockEntityPlateBox be)
            {
                be.SaveToItemStack(stack);
            }

            return stack;
        }

        // Spawns a serialized server-side drop and removes both block and BlockEntity state.
        public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f)
        {
            if (world == null || pos == null)
            {
                return;
            }

            if (world.Side == EnumAppSide.Server && (byPlayer == null || byPlayer.WorldData?.CurrentGameMode != EnumGameMode.Creative))
            {
                ItemStack? stack = CreateDropStack(world, pos);
                if (stack != null)
                {
                    world.SpawnItemEntity(stack, pos.ToVec3d().Add(0.5, 0.5, 0.5));
                }

                world.PlaySoundAt(Sounds?.GetBreakSound(byPlayer).Location, pos, 0.0, byPlayer);
            }

            SpawnBlockBrokenParticles(pos, byPlayer);
            world.BlockAccessor.SetBlock(0, pos);
            world.BlockAccessor.RemoveBlockEntity(pos);
        }

        // Restores slot data from placed stack and forces the runtime block state closed.
        public override void OnBlockPlaced(IWorldAccessor world, BlockPos blockPos, ItemStack byItemStack = null!)
        {
            base.OnBlockPlaced(world, blockPos, byItemStack);

            if (world?.BlockAccessor?.GetBlockEntity(blockPos) is not BlockEntityPlateBox be) return;

            // SetBlock() is used to swap closed/open variants; those transitions should not trigger placement SFX.
            if (byItemStack == null) return;

            be.LoadFromItemStack(byItemStack, world);

            be.SetOpen(false);

            if (world?.Side == EnumAppSide.Server)
            {
                world.PlaySoundAt(_woodThudSound, blockPos.X + 0.5, blockPos.Y + 0.5, blockPos.Z + 0.5, null, true, 16f, 1f);
            }
        }

        // Redirects placement to the facing variant whose open side points toward the player.
        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
        {
            // Choose a facing so the box's open side faces toward the player.
            string? currentFacing = Variant?["facing"];
            BlockFacing playerFacing = BlockFacing.HorizontalFromYaw(byPlayer?.Entity?.SidedPos?.Yaw ?? 0f);
            string desiredFacing = playerFacing.Opposite.Code;

            // If already the right variant (or no facing variant), place directly.
            if (string.IsNullOrEmpty(currentFacing) || currentFacing == desiredFacing)
                return base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);

            Block? facingBlock = world.GetBlock(new AssetLocation("phototesting", "platebox-" + desiredFacing));
            if (facingBlock == null || facingBlock.Id == 0)
                return base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);

            // Delegate to the correct facing variant's TryPlaceBlock.
            // That call will hit the "currentFacing == desiredFacing" branch above, so no recursion.
            return facingBlock.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);
        }

    }
}

