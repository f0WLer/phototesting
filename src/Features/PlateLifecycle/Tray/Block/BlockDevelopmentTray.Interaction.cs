using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Phototesting.AdminTooling;

namespace Phototesting.PlateLifecycle.Tray
{
    public sealed partial class BlockDevelopmentTray
    {
        // ── Interaction entry ────────────────────────────────────────────────────

        // Routes tray interaction start through side-aware client-prediction or server-authority paths.
        // Guards against re-entry while RMB is still held after a completed timed pour (client only).
        private bool HandleWorldObjectInteractionStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (world.Side == EnumAppSide.Client && TrayTimedInteractionState.NeedsRelease(byPlayer)) return false;

            if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BlockEntityDevelopmentTray be)
            {
                return false;
            }

            ItemSlot? activeSlot = byPlayer.InventoryManager?.ActiveHotbarSlot;
            ItemStack? held = activeSlot?.Itemstack;
            int chemicalUnitsPerUse = GetChemicalUnitsPerUse();

            if (world.Side == EnumAppSide.Client)
            {
                return HandleInteractStartClient(world, byPlayer, blockSel, be, activeSlot, held, chemicalUnitsPerUse);
            }

            return HandleInteractStartServer(world, byPlayer, blockSel, be, activeSlot, held, chemicalUnitsPerUse);
        }

        // ── Client-side prediction ───────────────────────────────────────────────

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
                    if (PlateDryingTransition.IsDry(world, clientDevPlate))
                    {
                        (world.Api as ICoreClientAPI)?.ShowChatMessage("Wetplate: the plate has dried and can no longer be used.");
                        return false;
                    }
                    if (!WetPlateChemicalUtil.HasConsumableChemical(activeSlot, spec.DeveloperPortionCode, spec.DeveloperAmountPerUse)) return false;

                    TrayTimedInteractionState.Begin(byPlayer, blockSel.Position, ActionDeveloper, GetDeveloperPourSeconds());
                    return true;
                }

                // Holding fixer: allow attempt when there's a developed plate (server will message if not ready).
                if (IsHoldingChemical(activeSlot, spec.FixerPortionCode))
                {
                    if (!TryGetFixerPourContext(be, spec.DeveloperApplicationsRequired, out ItemStack clientFixPlate, out _)) return false;
                    if (PlateDryingTransition.IsDry(world, clientFixPlate))
                    {
                        (world.Api as ICoreClientAPI)?.ShowChatMessage("Wetplate: the plate has dried and can no longer be used.");
                        return false;
                    }
                    if (!WetPlateChemicalUtil.HasConsumableChemical(activeSlot, spec.FixerPortionCode, spec.FixerAmountPerUse)) return false;

                    TrayTimedInteractionState.Begin(byPlayer, blockSel.Position, ActionFixer, GetFixerPourSeconds());
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

                TrayTimedInteractionState.Begin(byPlayer, blockSel.Position, ActionWater, GetWaterPourSeconds());
                return true;
            }

            return false;
        }

        // ── Server-authoritative start ───────────────────────────────────────────

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
                TrayTimedInteractionState.Begin(byPlayer, trayPos, actionContext.TimedActionKey, actionContext.DurationSeconds);
                return true;
            }

            return false;
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
                trayStage = PlateStateTransitions.IsDevelopingFamily(held)
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

        // ── Step / stop callbacks ────────────────────────────────────────────────

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
                        TrayTimedInteractionState.SetNeedsRelease(byPlayer);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug(world.Logger, "OnBlockInteractStop timed-release check failed: {0}", ex.Message);
            }

            TrayTimedInteractionState.Clear(byPlayer);
            base.OnBlockInteractStop(secondsUsed, world, byPlayer, blockSel);
        }

        // ── Timed interaction runtime ────────────────────────────────────────────

        // Handles timed tray runtime progression and applies the resolved action once the hold duration is met.
        private bool HandleTrayRuntimeTimedInteractionStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockPos pos, int chemicalUnitsPerUse)
        {
            if (!TryResolveTimedActionKind(byPlayer, pos, out TrayActionKind actionKind))
            {
                return false;
            }

            if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityDevelopmentTray be)
            {
                TrayTimedInteractionState.Clear(byPlayer);
                return false;
            }

            if (!TryResolveActionContextFromTimedState(actionKind, be, chemicalUnitsPerUse, out TrayActionContext actionContext))
            {
                TrayTimedInteractionState.Clear(byPlayer);
                return false;
            }

            ItemSlot? activeSlot = byPlayer.InventoryManager?.ActiveHotbarSlot;
            if (!ActionValidatorService.TryValidateTimedStepForAction(world, byPlayer, pos, be, activeSlot, actionContext, out ItemStack plate, out bool isExposed, out int currentApplications))
            {
                TrayTimedInteractionState.Clear(byPlayer);
                return false;
            }

            if (secondsUsed < actionContext.DurationSeconds) return true;

            // Latch until RMB release to prevent auto-starting the next action.
            if (world.Side == EnumAppSide.Client)
            {
                TrayTimedInteractionState.SetNeedsRelease(byPlayer);
                // ExposurePaused plate: seal now that the developer pour completed successfully.
                if (actionKind == TrayActionKind.Developer && PlateStateService.GetStage(plate) == PlateStage.ExposurePaused)
                    if (world.Api is ICoreClientAPI capiSeal)
                        PhotoTestingConfigAccess.ResolveModSystem(capiSeal)?.CameraCaptureBridge.TrySendSealForTray(capiSeal, pos, plate);
            }

            if (world.Side == EnumAppSide.Server)
            {
                if (!ActionApplierService.TryApplyTimedActionServer(this, world, byPlayer, pos, be, activeSlot, plate, isExposed, currentApplications, actionContext))
                {
                    TrayTimedInteractionState.Clear(byPlayer);
                    return false;
                }

                world.PlaySoundAt(_fizzSound, pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5, null, true, 16f, 1f);
            }

            TrayTimedInteractionState.Clear(byPlayer);
            return false;
        }

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
            if (TrayTimedInteractionState.IsActive(byPlayer, pos, ActionDeveloper))
            {
                actionKind = TrayActionKind.Developer;
                return true;
            }

            if (TrayTimedInteractionState.IsActive(byPlayer, pos, ActionFixer))
            {
                actionKind = TrayActionKind.Fixer;
                return true;
            }

            if (TrayTimedInteractionState.IsActive(byPlayer, pos, ActionWater))
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

        // ── Plate insert/take helpers ────────────────────────────────────────────

        // Executes shared server-side insert flow: facing capture, plate clone/insert, stage normalization, block swap, and slot consume.
        private bool TryInsertHeldPlateIntoTray(IWorldAccessor world, IPlayer byPlayer, BlockPos trayPos, BlockEntityDevelopmentTray be, ItemSlot activeSlot, ItemStack held, PlateStage trayStage)
        {
            // Ensure tray photo orientation always tracks the player who is actively using the tray.
            BlockFacing insertFacing = BlockFacing.HorizontalFromYaw(byPlayer?.Entity?.Pos?.Yaw ?? 0f);
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

        // Flags any dry wet-stage plate as reclaimable by a water rinse.
        private static bool IsReclaimablePlate(IWorldAccessor world, ItemStack? stack)
        {
            if (stack == null) return false;
            return PlateStateTransitions.IsWetStage(stack) && PlateDryingTransition.IsDry(world, stack);
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

        // ── Shared helpers ───────────────────────────────────────────────────────

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

            PlateStage plateStage = PlateStateService.GetStage(plate);
            // ExposurePaused is treated as exposed: the seal packet will stamp it before the pour fires.
            isExposed = plateStage == PlateStage.Exposed || plateStage == PlateStage.ExposurePaused;
            isDeveloped = plateStage == PlateStage.Developed;
            bool isDeveloping = plateStage == PlateStage.Developing;
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

        // Allows exposed, paused-exposure, developing, and developed plates to enter the tray workflow.
        private static bool IsInsertablePlate(ItemStack? stack)
        {
            if (stack == null) return false;

            PlateStage stage = PlateStateService.GetStage(stack);
            return stage == PlateStage.Exposed
                || stage == PlateStage.ExposurePaused
                || PlateStateTransitions.IsDevelopingFamily(stack);
        }

        // Identifies a dry sensitized plate that can still be reclaimed with a water rinse.
        private static bool IsDrySensitizedForReclaim(IWorldAccessor world, ItemStack? stack)
        {
            if (stack == null || !PlateDryingTransition.IsDry(world, stack)) return false;

            return PlateStateService.GetStage(stack) == PlateStage.Sensitized;
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

        // ── Held-help ────────────────────────────────────────────────────────────

        // Keeps engine callback ownership local and delegates held-help composition to the interaction logic.
        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
        {
            return BuildWorldObjectInteractionHelp(world, selection);
        }

        // Builds tray held-help list from current plate state so prompts stay aligned with workflow.
        private WorldInteraction[] BuildWorldObjectInteractionHelp(IWorldAccessor world, BlockSelection selection)
        {
            if (world == null || selection == null) return System.Array.Empty<WorldInteraction>();

            var interactions = new List<WorldInteraction>();

            BlockPos pos = selection.Position;
            if (pos == null) return System.Array.Empty<WorldInteraction>();

            BlockEntityDevelopmentTray? be = world.BlockAccessor.GetBlockEntity(pos) as BlockEntityDevelopmentTray;
            ItemStack? plate = be?.PlateStack;

            if (plate == null)
            {
                AddInsertPlateInteractions(world, interactions);
                return interactions.ToArray();
            }

            AddPlatePresentInteractions(world, be, plate, interactions);
            return interactions.ToArray();
        }

        // Adds the insert prompts shown when the tray is empty.
        private void AddInsertPlateInteractions(IWorldAccessor world, List<WorldInteraction> interactions)
        {
            var sensitizedItem = world.GetItem(_sensitizedPlateItemCode);
            var photoItem = world.GetItem(_photoPlateItemCode);

            var stacks = new List<ItemStack>();
            if (sensitizedItem != null)
            {
                ItemStack stack = new ItemStack(sensitizedItem);
                PlateStateService.SetStage(stack, PlateStage.Exposed);
                stacks.Add(stack);
            }

            if (photoItem != null)
            {
                ItemStack stack = new ItemStack(photoItem);
                PlateStateService.SetStage(stack, PlateStage.Developed);
                stacks.Add(stack);
            }

            if (stacks.Count <= 0) return;

            interactions.Add(new WorldInteraction
            {
                ActionLangCode = "phototesting:heldhelp-developmenttray-insertplate",
                MouseButton = EnumMouseButton.Right,
                Itemstacks = stacks.ToArray()
            });
        }

        // Adds take, develop, and fix prompts for the plate currently sitting in the tray.
        private void AddPlatePresentInteractions(IWorldAccessor world, BlockEntityDevelopmentTray? be, ItemStack plate, List<WorldInteraction> interactions)
        {
            interactions.Add(new WorldInteraction
            {
                ActionLangCode = "phototesting:heldhelp-developmenttray-takeplate",
                MouseButton = EnumMouseButton.Right
            });

            bool canDevelop;
            bool canFix;
            TrayDevelopmentSpec? spec = null;

            if (be != null)
            {
                spec = ResolveDevelopmentSpec(be.PlateStack, GetChemicalUnitsPerUse());

                canDevelop = TryGetDeveloperPourContext(be, spec.Value.DeveloperApplicationsRequired, out _, out _, out _, out int currentPours)
                    && currentPours < spec.Value.DeveloperApplicationsRequired;

                canFix = TryGetFixerPourContext(be, spec.Value.DeveloperApplicationsRequired, out _, out int pours)
                    && pours >= spec.Value.DeveloperApplicationsRequired;
            }
            else
            {
                canDevelop = PlateStateService.GetStage(plate) == PlateStage.Exposed
                    || PlateStateService.GetStage(plate) == PlateStage.Developing;
                canFix = PlateStateService.GetStage(plate) == PlateStage.Developed;
            }

            if (canDevelop)
            {
                AssetLocation developerCode = spec?.DeveloperPortionCode ?? _developerPortionCode;
                int developerAmount = spec?.DeveloperAmountPerUse ?? GetChemicalUnitsPerUse();
                AddChemicalInteraction(world, interactions, "phototesting:heldhelp-developmenttray-develop", developerCode, developerAmount);
            }

            if (canFix)
            {
                AssetLocation fixerCode = spec?.FixerPortionCode ?? _fixerPortionCode;
                int fixerAmount = spec?.FixerAmountPerUse ?? GetChemicalUnitsPerUse();
                AddChemicalInteraction(world, interactions, "phototesting:heldhelp-developmenttray-fix", fixerCode, fixerAmount);
            }
        }

        // Adds one held-help interaction for a chemical item when the item code resolves in the current world.
        private static void AddChemicalInteraction(IWorldAccessor world, List<WorldInteraction> interactions, string actionLangCode, AssetLocation itemCode, int amount)
        {
            Item? item = world.GetItem(itemCode);
            if (item == null) return;

            interactions.Add(new WorldInteraction
            {
                ActionLangCode = actionLangCode,
                MouseButton = EnumMouseButton.Right,
                Itemstacks = new[] { new ItemStack(item, amount) }
            });
        }

        // ── Context struct ───────────────────────────────────────────────────────

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
    }
}
