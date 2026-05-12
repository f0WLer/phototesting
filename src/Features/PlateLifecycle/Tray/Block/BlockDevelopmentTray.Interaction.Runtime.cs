using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Phototesting.PlateLifecycle.Tray
{
    // World-object interaction: shared runtime, helpers, and integration seams.
    public sealed partial class BlockDevelopmentTray
    {

        // Resolves the tray action context directly from the chemical the player is currently holding.
        private bool TryResolveActionContextFromHeldChemical(BlockEntityDevelopmentTray be, ItemSlot? activeSlot, int chemicalUnitsPerUse, out TrayActionContext context)
        {
            TrayDevelopmentSpec spec = ResolveDevelopmentSpec(be.PlateStack, chemicalUnitsPerUse);

            if (TrayActionResolver.TryResolveHeldChemicalAction(activeSlot, spec, out TrayActionKind actionKind))
            {
                context = CreateActionContext(actionKind, spec);
                return true;
            }

            context = default;
            return false;
        }

        // Rebuilds the tray action context from persisted timed-interaction state on subsequent step callbacks.
        private bool TryResolveActionContextFromTimedState(TrayActionKind actionKind, BlockEntityDevelopmentTray be, int chemicalUnitsPerUse, out TrayActionContext context)
        {
            context = default;
            TrayDevelopmentSpec spec = ResolveDevelopmentSpec(be.PlateStack, chemicalUnitsPerUse);
            if (!TrayActionResolver.IsTimedActionAllowed(actionKind, spec)) return false;

            context = CreateActionContext(actionKind, spec);
            return true;
        }

        // Converts the timed-interaction attributes back into a concrete tray action kind.
        private static bool TryResolveTimedActionKind(IPlayer byPlayer, BlockPos pos, out TrayActionKind actionKind)
        {
            if (IsTimed(byPlayer, pos, ActionDeveloper))
            {
                actionKind = TrayActionKind.Developer;
                return true;
            }

            if (IsTimed(byPlayer, pos, ActionFixer))
            {
                actionKind = TrayActionKind.Fixer;
                return true;
            }

            if (IsTimed(byPlayer, pos, ActionWater))
            {
                actionKind = TrayActionKind.Water;
                return true;
            }

            actionKind = default;
            return false;
        }

        // Creates a normalized tray action context so downstream validation and apply code can stay generic.
        private TrayActionContext CreateActionContext(TrayActionKind actionKind, TrayDevelopmentSpec spec)
        {
            return actionKind switch
            {
                TrayActionKind.Developer => new TrayActionContext(
                    kind: actionKind,
                    spec: spec,
                    timedActionKey: ActionDeveloper,
                    durationSeconds: GetDeveloperPourSeconds(),
                    portionCode: spec.DeveloperPortionCode,
                    amountPerUse: spec.DeveloperAmountPerUse),
                TrayActionKind.Fixer => new TrayActionContext(
                    kind: actionKind,
                    spec: spec,
                    timedActionKey: ActionFixer,
                    durationSeconds: GetFixerPourSeconds(),
                    portionCode: spec.FixerPortionCode,
                    amountPerUse: spec.FixerAmountPerUse),
                _ => new TrayActionContext(
                    kind: TrayActionKind.Water,
                    spec: spec,
                    timedActionKey: ActionWater,
                    durationSeconds: GetWaterPourSeconds(),
                    portionCode: spec.WaterPortionCode,
                    amountPerUse: spec.WaterAmountPerUse)
            };
        }

        // Returns the effective timed duration for the given tray action kind.
        private float GetDurationSeconds(TrayActionKind actionKind)
        {
            return actionKind switch
            {
                TrayActionKind.Developer => GetDeveloperPourSeconds(),
                TrayActionKind.Fixer => GetFixerPourSeconds(),
                _ => GetWaterPourSeconds()
            };
        }

        // Provides the user-facing missing-chemical message for each tray action kind.
        private static string GetMissingChemicalMessage(TrayActionKind actionKind)
        {
            return actionKind switch
            {
                TrayActionKind.Developer => "Wetplate: need developer (at least 1 portion).",
                TrayActionKind.Fixer => "Wetplate: need fixer (at least 1 portion).",
                _ => "Wetplate: need water (at least 1 portion)."
            };
        }

        // Handles timed tray runtime progression and applies the resolved action once the hold duration is met.
        private bool HandleTrayRuntimeTimedInteractionStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockPos pos, int chemicalUnitsPerUse)
        {
            if (!TryResolveTimedActionKind(byPlayer, pos, out TrayActionKind actionKind))
            {
                return false;
            }

            if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityDevelopmentTray be)
            {
                ClearTimed(byPlayer);
                return false;
            }

            if (!TryResolveActionContextFromTimedState(actionKind, be, chemicalUnitsPerUse, out TrayActionContext actionContext))
            {
                ClearTimed(byPlayer);
                return false;
            }

            ItemSlot? activeSlot = byPlayer.InventoryManager?.ActiveHotbarSlot;
            if (!ActionValidatorService.TryValidateTimedStepForAction(world, byPlayer, pos, be, activeSlot, actionContext, out ItemStack plate, out bool isExposed, out int currentApplications))
            {
                ClearTimed(byPlayer);
                return false;
            }

            if (secondsUsed < actionContext.DurationSeconds) return true;

            // Latch until RMB release to prevent auto-starting the next action.
            if (world.Side == EnumAppSide.Client) SetNeedsRelease(byPlayer);

            if (world.Side == EnumAppSide.Server)
            {
                if (!ActionApplierService.TryApplyTimedActionServer(this, world, byPlayer, pos, be, activeSlot, plate, isExposed, currentApplications, actionContext))
                {
                    ClearTimed(byPlayer);
                    return false;
                }

                world.PlaySoundAt(_fizzSound, pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5, null, true, 16f, 1f);
            }

            ClearTimed(byPlayer);
            return false;
        }
    }
}

