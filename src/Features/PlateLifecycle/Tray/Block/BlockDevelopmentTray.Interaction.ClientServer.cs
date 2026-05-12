using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Phototesting.PlateLifecycle.Tray
{
    // World-object interaction: client predicted handling and server authoritative response.
    public sealed partial class BlockDevelopmentTray
    {
        // Client-side prediction gate for tray interactions so timing visuals and held-help feel immediate before server authority responds.
        private bool HandleInteractStartClient(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, BlockEntityDevelopmentTray be, ItemSlot? activeSlot, ItemStack? held, int chemicalUnitsPerUse)
        {
            // Empty hand: take plate out.
            if (held == null)
            {
                return be.HasPlate;
            }

            // Holding a plate: insert (only if tray empty). Dry plates still insertable for water rinse.
            if (IsInsertablePlate(held))
            {
                return !be.HasPlate;
            }

            // Holding a dry sensitized plate: insert for water rinse.
            if (IsDrySensitizedForReclaim(world, held))
            {
                return !be.HasPlate;
            }

            // Holding developer: can attempt timed pour when tray has an exposed/developed plate.
            if (be.HasPlate)
            {
                TrayDevelopmentSpec spec = ResolveDevelopmentSpec(be.PlateStack, chemicalUnitsPerUse);

                if (IsHoldingChemical(activeSlot, spec.DeveloperPortionCode))
                {
                    if (!TryGetDeveloperPourContext(be, spec.DeveloperApplicationsRequired, out ItemStack clientDevPlate, out _, out _, out int currentPours)) return false;

                    if (currentPours >= spec.DeveloperApplicationsRequired) return false;
                    if (WetPlateAttrs.IsDry(world, clientDevPlate))
                    {
                        (world.Api as ICoreClientAPI)?.ShowChatMessage("Wetplate: the plate has dried and can no longer be used.");
                        return false;
                    }
                    if (!WetPlateChemicalUtil.HasConsumableChemical(activeSlot, spec.DeveloperPortionCode, spec.DeveloperAmountPerUse)) return false;

                    // Prime local timed state so client-only visuals can react immediately.
                    BeginTimed(byPlayer, blockSel.Position, ActionDeveloper, GetDeveloperPourSeconds());
                    return true;
                }

                // Holding fixer: allow attempt when there's a developed plate (server will message if not ready).
                if (IsHoldingChemical(activeSlot, spec.FixerPortionCode))
                {
                    if (!TryGetFixerPourContext(be, spec.DeveloperApplicationsRequired, out ItemStack clientFixPlate, out _)) return false;
                    if (WetPlateAttrs.IsDry(world, clientFixPlate))
                    {
                        (world.Api as ICoreClientAPI)?.ShowChatMessage("Wetplate: the plate has dried and can no longer be used.");
                        return false;
                    }
                    if (!WetPlateChemicalUtil.HasConsumableChemical(activeSlot, spec.FixerPortionCode, spec.FixerAmountPerUse)) return false;

                    // Prime local timed state so client-only visuals can react immediately.
                    BeginTimed(byPlayer, blockSel.Position, ActionFixer, GetFixerPourSeconds());
                    return true;
                }
            }

            // Holding water: can attempt rinse when tray has a dry plate.
            TrayDevelopmentSpec waterSpec = ResolveDevelopmentSpec(be.PlateStack, chemicalUnitsPerUse);
            if (!waterSpec.HasWaterRinseStep) return false;

            if (IsHoldingChemical(activeSlot, waterSpec.WaterPortionCode))
            {
                if (!TryGetReclaimContext(be, world, out _)) return false;
                if (!WetPlateChemicalUtil.HasConsumableChemical(activeSlot, waterSpec.WaterPortionCode, waterSpec.WaterAmountPerUse)) return false;

                BeginTimed(byPlayer, blockSel.Position, ActionWater, GetWaterPourSeconds());
                return true;
            }

            return false;
        }
        // Bundles the resolved timing and chemical requirements for one tray action so the server path can stay process-aware.
        private readonly struct TrayActionContext
        {
            public TrayActionKind Kind { get; }
            public TrayDevelopmentSpec Spec { get; }
            public string TimedActionKey { get; }
            public float DurationSeconds { get; }
            public AssetLocation PortionCode { get; }
            public int AmountPerUse { get; }

            public TrayActionContext(TrayActionKind kind, TrayDevelopmentSpec spec, string timedActionKey, float durationSeconds, AssetLocation portionCode, int amountPerUse)
            {
                Kind = kind;
                Spec = spec;
                TimedActionKey = timedActionKey;
                DurationSeconds = durationSeconds;
                PortionCode = portionCode;
                AmountPerUse = amountPerUse;
            }
        }

        // Normalizes held-plate insert routing so server interaction start stays orchestration-only.
        private bool TryHandlePlateInsertServer(IWorldAccessor world, IPlayer byPlayer, BlockPos trayPos, BlockEntityDevelopmentTray be, ItemSlot? activeSlot, ItemStack held)
        {
            if (be.HasPlate || activeSlot == null) return false;

            if (TryResolveInsertStage(world, held, out PlateStage trayStage))
            {
                return TryInsertHeldPlateIntoTray(world, byPlayer, trayPos, be, activeSlot, held, trayStage);
            }

            return false;
        }

        // Maps insert-eligible held plates into the tray stage they should normalize to on entry.
        private static bool TryResolveInsertStage(IWorldAccessor world, ItemStack held, out PlateStage trayStage)
        {
            if (IsInsertablePlate(held))
            {
                trayStage = PlateLifecycleStateCoordinator.IsDevelopingFamily(held)
                    ? PlateStage.Developed
                    : PlateStage.Exposed;
                return true;
            }

            if (IsDrySensitizedForReclaim(world, held))
            {
                trayStage = PlateStage.Exposed;
                return true;
            }

            trayStage = default;
            return false;
        }

        // Authoritative tray interaction entry point for inserting plates, removing plates, and starting timed pours.
        private bool HandleInteractStartServer(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, BlockEntityDevelopmentTray be, ItemSlot? activeSlot, ItemStack? held, int chemicalUnitsPerUse)
        {
            BlockPos trayPos = blockSel.Position;

            // Empty hand: take plate out.
            if (held == null)
            {
                return TryTakePlateOutServer(world, byPlayer, trayPos, be);
            }

            // Plate-like held items normalize through one insert path, including dry reclaim cases.
            if (TryHandlePlateInsertServer(world, byPlayer, trayPos, be, activeSlot, held))
            {
                return true;
            }

            // Chemical interactions: developer/fixer/water are all evaluated via one process-aware action context.
            if (TryResolveActionContextFromHeldChemical(be, activeSlot, chemicalUnitsPerUse, out TrayActionContext actionContext))
            {
                if (!ActionValidatorService.TryValidateStartForAction(world, byPlayer, trayPos, be, activeSlot, actionContext))
                {
                    return false;
                }

                world.PlaySoundAt(_chemicalPourSound, trayPos.X + 0.5, trayPos.Y + 0.5, trayPos.Z + 0.5, null);
                BeginTimed(byPlayer, trayPos, actionContext.TimedActionKey, actionContext.DurationSeconds);
                return true;
            }

            return false;
        }

        // Shared server empty-hand flow: remove current tray plate, normalize block variant, and return item to player.
        private bool TryTakePlateOutServer(IWorldAccessor world, IPlayer byPlayer, BlockPos trayPos, BlockEntityDevelopmentTray be)
        {
            if (!be.HasPlate) return false;

            ItemStack? taken = be.TakePlate();
            if (taken == null) return false;

            SwapTrayBlockForPlateStage(world, trayPos, null, null);
            GiveOrDrop(world, byPlayer, taken, trayPos);
            return true;
        }

        // Tracks an active timed pour until completion and then applies the resolved developer, fixer, or water result on the server.
        public override bool OnBlockInteractStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (world == null || byPlayer == null || blockSel == null) return false;
            return HandleTrayRuntimeTimedInteractionStep(secondsUsed, world, byPlayer, blockSel.Position, GetChemicalUnitsPerUse());
        }

        // Clears tray timed state on stop while preserving the RMB-release latch for completed client actions.
        public override void OnBlockInteractStop(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (byPlayer == null) return;

            // If a timed action completed, latch until RMB is actually released.
            // (OnBlockInteractStop may fire both on completion and on RMB release, so do NOT clear here.)
            try
            {
                if (world?.Side == EnumAppSide.Client && blockSel?.Position != null)
                {
                    BlockPos pos = blockSel.Position;
                    if (TryResolveTimedActionKind(byPlayer, pos, out TrayActionKind actionKind) && secondsUsed >= GetDurationSeconds(actionKind))
                    {
                        SetNeedsRelease(byPlayer);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug(world.Logger, "OnBlockInteractStop timed-release check failed: {0}", ex.Message);
            }

            // Clear any in-progress timed interaction for this player.
            ClearTimed(byPlayer);
            base.OnBlockInteractStop(secondsUsed, world, byPlayer, blockSel);
        }

        // Flags any dry wet-stage plate as reclaimable by a water rinse.
        private static bool IsReclaimablePlate(IWorldAccessor world, ItemStack? stack)
        {
            if (stack == null) return false;
            return PlateLifecycleStateCoordinator.IsWetStage(stack) && WetPlateAttrs.IsDry(world, stack);
        }

        // Extracts the tray plate only when it qualifies for the reclaim-with-water path.
        private static bool TryGetReclaimContext(BlockEntityDevelopmentTray be, IWorldAccessor world, out ItemStack plate)
        {
            ItemStack? plateStack = be.PlateStack;
            if (plateStack == null || !IsReclaimablePlate(world, plateStack))
            {
                plate = null!;
                return false;
            }
            plate = plateStack;
            return true;
        }

        // Water rinse timing is fixed rather than process-driven.
        private static float GetWaterPourSeconds() => 1.25f;

        // Executes shared server-side insert flow: facing capture, plate clone/insert, stage normalization, block swap, and slot consume.
        private bool TryInsertHeldPlateIntoTray(IWorldAccessor world, IPlayer byPlayer, BlockPos trayPos, BlockEntityDevelopmentTray be, ItemSlot activeSlot, ItemStack held, PlateStage trayStage)
        {
            // Ensure tray photo orientation always tracks the player who is actively using the tray.
            // This acts as a reliable fallback if placement-time facing capture is unavailable.
            BlockFacing insertFacing = BlockFacing.HorizontalFromYaw(byPlayer?.Entity?.SidedPos?.Yaw ?? 0f);
            be.SetPlacementFacing(insertFacing.Code, markBlockDirty: false);

            ItemStack toInsert = held.Clone();
            toInsert.StackSize = 1;

            if (!be.TryInsertPlate(toInsert)) return false;

            PlateStateService.EnsureProcessId(toInsert);
            PlateStateService.EnsureStage(toInsert, trayStage);
            SwapTrayBlockForPlateStage(world, trayPos, PlateStageUtil.ToAttributeString(trayStage), toInsert);

            activeSlot.TakeOut(1);
            activeSlot.MarkDirty();
            return true;
        }

    }
}
