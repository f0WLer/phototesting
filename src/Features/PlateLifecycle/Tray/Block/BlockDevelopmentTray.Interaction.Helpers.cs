using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Phototesting.PlateLifecycle.Tray
{
    // World-object interaction: shared runtime, helpers, and integration seams.
    public sealed partial class BlockDevelopmentTray
    {

    // Shared tray interaction helpers: timed state delegation, chemical slot checks, plate stage
    // context resolution, block swap, notification, plate predicate utilities, and item delivery.
    // Side: both (each method is internally gated where side matters).
    // Related files: BlockDevelopmentTray.PlateLifecycleServer.cs, BlockDevelopmentTray.PlateLifecycleClient.cs, BlockDevelopmentTray.PlateLifecycleTrayRuntime.Apply.cs.

        // Architectural seam policy: timed-state calls stay wrapped here so tray interaction code
        // can evolve independently from TrayTimedInteractionState storage/details.

        // Records client/server timed-action state so the tray can track a held chemical pour across callbacks.
        private static void BeginTimed(IPlayer byPlayer, BlockPos pos, string action, float durationSeconds)
        {
            TrayTimedInteractionState.Begin(byPlayer, pos, action, durationSeconds);
        }

        // Checks whether the player is mid-pour at this tray.
        private static bool IsTimed(IPlayer byPlayer, BlockPos pos, string action)
        {
            return TrayTimedInteractionState.IsActive(byPlayer, pos, action);
        }

        // Clears all tray timed-interaction state for the player.
        private static void ClearTimed(IPlayer byPlayer)
        {
            TrayTimedInteractionState.Clear(byPlayer);
        }

        // Checks the RMB-release latch.
        private static bool NeedsRelease(IPlayer byPlayer)
        {
            return TrayTimedInteractionState.NeedsRelease(byPlayer);
        }

        // Latches the timed interaction until RMB release so the next pour does not auto-start immediately.
        private static void SetNeedsRelease(IPlayer byPlayer)
        {
            TrayTimedInteractionState.SetNeedsRelease(byPlayer);
        }

        // Treats direct portions and compatible containers as valid held chemicals for tray actions.
        private static bool IsHoldingChemical(ItemSlot? slot, AssetLocation code)
        {
            return slot?.Itemstack != null && WetPlateChemicalUtil.IsChemicalOrContainerWith(slot.Itemstack, code);
        }

        // Expands the plate's active process into tray-specific developer/fixer/water requirements.
        private TrayDevelopmentSpec ResolveDevelopmentSpec(ItemStack? plate, int waterUnitsPerUse)
        {
            return TrayDevelopmentSpecResolver.ResolveDevelopmentSpec(
                api,
                plate,
                waterUnitsPerUse,
                _developerPortionCode,
                _fixerPortionCode,
                _waterPortionCode);
        }

        // Validates that the inserted plate is in a developer-eligible stage and reports the current pour count.
        private static bool TryGetDeveloperPourContext(BlockEntityDevelopmentTray be, int developerPourCount, out ItemStack plate, out bool isExposed, out bool isDeveloped, out int currentPours)
        {
            ItemStack? plateStack = be.PlateStack;
            if (plateStack == null)
            {
                plate = null!;
                isExposed = false;
                isDeveloped = false;
                currentPours = 0;
                return false;
            }

            plate = plateStack;

            isExposed = PlateStateService.GetStage(plate) == PlateStage.Exposed;
            isDeveloped = PlateStateService.GetStage(plate) == PlateStage.Developed;
            bool isDeveloping = PlateStateService.GetStage(plate) == PlateStage.Developing;
            if (!isExposed && !isDeveloped && !isDeveloping)
            {
                currentPours = 0;
                return false;
            }

            if (isDeveloped)
            {
                currentPours = developerPourCount;
            }
            else if (isDeveloping)
            {
                currentPours = PlateDevelopmentService.GetCurrentStepApplications(plate);
            }
            else
            {
                currentPours = 0;
            }

            if (currentPours < 0) currentPours = 0;
            if (currentPours > developerPourCount) currentPours = developerPourCount;
            return true;
        }

        // Validates that the inserted plate is ready for fixer and returns the tracked developer completion count.
        private static bool TryGetFixerPourContext(BlockEntityDevelopmentTray be, int developerPourCount, out ItemStack plate, out int pours)
        {
            ItemStack? plateStack = be.PlateStack;
            if (plateStack == null)
            {
                plate = null!;
                pours = 0;
                return false;
            }

            plate = plateStack;

            bool isDeveloped = PlateStateService.GetStage(plate) == PlateStage.Developed;
            if (!isDeveloped)
            {
                pours = 0;
                return false;
            }

            pours = developerPourCount;
            return true;
        }

        // Swaps the tray block variant to match the plate stage and then restores the carried plate stack onto the recreated block entity.
        private void SwapTrayBlockForPlateStage(IWorldAccessor world, BlockPos pos, string? stage, ItemStack? plateToKeep)
        {
            if (world == null || pos == null || Code == null) return;

            string placementFacing = "east";
            if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityDevelopmentTray oldBe)
            {
                placementFacing = oldBe.PlacementFacingCode;
            }

            string path = Code.Path;
            if (!path.StartsWith("developmenttray-")) return;

            string rest = path.Substring("developmenttray-".Length);
            int dash = rest.IndexOf('-');
            string clay = dash >= 0 ? rest.Substring(0, dash) : rest;

            AssetLocation targetLoc = stage == null
                ? new AssetLocation(Code.Domain, $"developmenttray-{clay}")
                : new AssetLocation(Code.Domain, $"developmenttray-{clay}-{stage}");

            Block? target = world.GetBlock(targetLoc);
            if (target == null) return;

            int targetId = target.Id;
            if (targetId <= 0) return;

            world.BlockAccessor.SetBlock(targetId, pos);

            // Reapply the plate stack after swapping blocks (BE can be recreated).
            if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityDevelopmentTray newBe)
            {
                newBe.SetPlacementFacing(placementFacing, markBlockDirty: false);

                if (plateToKeep != null)
                {
                    newBe.TrySetPlate(plateToKeep);
                }
            }
            else if (plateToKeep != null)
            {
                world.SpawnItemEntity(plateToKeep, pos.ToVec3d().Add(0.5, 0.5, 0.5));
            }
        }

        // Sends a concise server notification tied to the tray interaction outcome.
        private static void Tell(IPlayer byPlayer, string message, BlockPos pos)
        {
            if (byPlayer is IServerPlayer sp)
            {
                sp.SendMessage(GlobalConstants.GeneralChatGroup, message, EnumChatType.Notification);
            }
        }

        // Allows exposed, developing, and developed plates to enter the tray workflow.
        private static bool IsInsertablePlate(ItemStack? stack)
        {
            if (stack == null) return false;

            return PlateStateService.GetStage(stack) == PlateStage.Exposed
                || PlateLifecycleStateCoordinator.IsDevelopingFamily(stack);
        }

        // Identifies a dry sensitized plate that can still be reclaimed with a water rinse.
        private static bool IsDrySensitizedForReclaim(IWorldAccessor world, ItemStack? stack)
        {
            if (stack == null || !WetPlateAttrs.IsDry(world, stack)) return false;

            return PlateStateService.GetStage(stack) == PlateStage.Sensitized;
        }

        // Gives the stack to the player when possible and otherwise drops it at the tray position.
        private static void GiveOrDrop(IWorldAccessor world, IPlayer byPlayer, ItemStack stack, BlockPos pos)
        {
            if (byPlayer is IServerPlayer sp)
            {
                if (!sp.InventoryManager.TryGiveItemstack(stack))
                {
                    world.SpawnItemEntity(stack, pos.ToVec3d().Add(0.5, 0.5, 0.5));
                }
                return;
            }

            world.SpawnItemEntity(stack, pos.ToVec3d().Add(0.5, 0.5, 0.5));
        }
    }
}
