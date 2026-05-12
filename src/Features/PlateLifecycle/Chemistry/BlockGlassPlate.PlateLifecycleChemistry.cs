using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Phototesting.PlateLifecycle.Blocks
{
    public sealed partial class BlockGlassPlate
    {
        // Handles timed ground-plate sensitization progression once interaction state has been identified as clean/coated.
        private bool HandleGroundSensitizationInteractionStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockPos pos, string state)
        {
            if (!TryGetGroundSensitizationContext(byPlayer, pos, state, out ItemSlot? chemicalSlot, out ItemStack plate, out PhotographyProcessDefinition process, out SensitizationStep step, out int nextStepIndex))
            {
                return false;
            }

            if (secondsUsed < ResolveSensitizationStepSeconds(step))
            {
                return true;
            }

            if (world.Side == EnumAppSide.Server)
            {
                if (byPlayer is not IServerPlayer sp) return false;
                return TryApplyGroundSensitizationStep(world, sp, pos, state, chemicalSlot, plate, process, step, nextStepIndex);
            }

            return false;
        }

        // Resolves the timed interaction length for the next sensitization step, including dry waits.
        private float ResolveSensitizationStepSeconds(SensitizationStep step)
        {
            if (step.Kind == SensitizationStepKind.Dry)
            {
                float seconds = step.WaitSeconds ?? GetSensitizationDrySeconds();
                if (seconds < 0f) seconds = 0f;
                if (seconds > 300f) seconds = 300f;
                return seconds;
            }

            return GetSensitizationPourSeconds();
        }

        // Quick client/server gate used to decide whether a ground plate can start the next sensitization step.
        private bool CanStartGroundSensitization(IPlayer player, BlockPos pos, string state, out SensitizationStep nextStep)
        {
            nextStep = default!;
            if (!TryGetGroundSensitizationContext(player, pos, state, out _, out _, out _, out var step, out _))
            {
                return false;
            }

            nextStep = step;
            return true;
        }

        // Collects the plate, active chemical slot, process definition, and next step needed to advance a placed plate.
        private bool TryGetGroundSensitizationContext(
            IPlayer player,
            BlockPos pos,
            string state,
            out ItemSlot? chemicalSlot,
            out ItemStack plate,
            out PhotographyProcessDefinition process,
            out SensitizationStep step,
            out int nextStepIndex)
        {
            chemicalSlot = player.InventoryManager?.ActiveHotbarSlot;
            plate = default!;
            process = ProcessRegistry.DefaultProcess;
            step = default!;
            nextStepIndex = -1;

            if (api?.ModLoader?.GetModSystem<PhotoTestingModSystem>() is not PhotoTestingModSystem modSys) return false;
            if (!TryCreateVirtualPlateStackFromPlacedState(api.World, pos, state, out plate)) return false;

            return PlateSensitizationService.TryResolveNextStep(modSys.Processes, plate, chemicalSlot, out process, out step, out nextStepIndex);
        }

        // Applies the resolved sensitization step, either advancing in place or replacing the block with the final sensitized plate item.
        private bool TryApplyGroundSensitizationStep(
            IWorldAccessor world,
            IServerPlayer player,
            BlockPos pos,
            string state,
            ItemSlot? chemicalSlot,
            ItemStack plate,
            PhotographyProcessDefinition process,
            SensitizationStep step,
            int nextStepIndex)
        {
            bool isCreative = player.WorldData?.CurrentGameMode == EnumGameMode.Creative;
            if (step.Kind == SensitizationStepKind.Chemical)
            {
                if (chemicalSlot == null || chemicalSlot.Itemstack == null) return false;
                if (!isCreative && !PlateSensitizationService.TryConsumeChemicalStep(chemicalSlot, step))
                {
                    return false;
                }
            }
            else if (step.Kind != SensitizationStepKind.Dry)
            {
                return false;
            }

            if (!PlateSensitizationService.TryAdvancePlateState(plate, process, nextStepIndex, out bool complete))
            {
                return false;
            }

            if (complete)
            {
                return GiveSensitizedPlateAndRemoveBlock(world, player, pos, plate, process);
            }

            Block? coatedBlock = GetBlockForState(world, "coated");
            if (coatedBlock == null) return false;

            world.BlockAccessor.SetBlock(coatedBlock.Id, pos);
            world.BlockAccessor.MarkBlockDirty(pos);
            TrySetPlacedPlateProcessState(world, pos, process.Id, nextStepIndex);
            return true;
        }

        // Reconstructs a virtual plate stack from the placed block so shared plate services can run without block-specific branching.
        private bool TryCreateVirtualPlateStackFromPlacedState(IWorldAccessor world, BlockPos pos, string state, out ItemStack plate)
        {
            plate = default!;
            Item? cleanPlate = world.GetItem(_glassPlateItemCode);
            if (cleanPlate == null) return false;

            plate = new ItemStack(cleanPlate, 1);

            if (TryGetPlacedPlateProcessState(world, pos, out string processId, out int stepIndex))
            {
                PlateLifecycleStateCoordinator.ApplyProcessProgress(plate, processId, stepIndex);
                PlateLifecycleStateCoordinator.SetStageAndName(plate, PlateStage.Sensitizing, null);
                return true;
            }

            return state != "coated";
        }

        // Builds the held-help hint for the next valid ground-plate sensitization action.
        private bool TryGetGroundSensitizationHelp(IWorldAccessor world, BlockPos? pos, string state, IPlayer? player, out WorldInteraction interaction)
        {
            interaction = default!;
            if (pos == null) return false;
            if (player == null) return false;

            if (!TryGetGroundSensitizationContext(player, pos, state, out _, out _, out PhotographyProcessDefinition process, out SensitizationStep step, out int nextStepIndex))
            {
                return false;
            }

            if (step.Kind == SensitizationStepKind.Dry)
            {
                interaction = new WorldInteraction
                {
                    ActionLangCode = "phototesting:heldhelp-plate-sensitize-dry",
                    MouseButton = EnumMouseButton.Right
                };

                return true;
            }

            string actionLangCode = state == "clean"
                ? "phototesting:heldhelp-coatglassplate"
                : "phototesting:heldhelp-plate-sensitize-next";

            return TryBuildGroundStepInteraction(world, process, nextStepIndex, actionLangCode, out interaction);
        }

        // Reads persisted process progress from the placed plate block entity.
        private static bool TryGetPlacedPlateProcessState(IWorldAccessor world, BlockPos pos, out string processId, out int stepIndex)
        {
            processId = string.Empty;
            stepIndex = -1;

            var be = world.BlockAccessor.GetBlockEntity(pos) as BlockEntityPlateProcessState;
            if (be == null || !be.HasProcessState) return false;

            processId = be.ProcessId;
            stepIndex = be.SensitizationStepIndex;
            return true;
        }

        // Persists the active process id and sensitization step index back onto the placed plate block entity.
        private static bool TrySetPlacedPlateProcessState(IWorldAccessor world, BlockPos pos, string processId, int stepIndex)
        {
            var be = world.BlockAccessor.GetBlockEntity(pos) as BlockEntityPlateProcessState;
            if (be == null) return false;

            be.SetProcessState(processId, stepIndex);
            world.BlockAccessor.MarkBlockEntityDirty(pos);
            return true;
        }

        // Creates a help interaction for the specified chemical step when the required portion item can be resolved.
        private static bool TryBuildGroundStepInteraction(IWorldAccessor world, PhotographyProcessDefinition process, int stepIndex, string actionLangCode, out WorldInteraction interaction)
        {
            interaction = default!;
            if (!PlateSensitizationService.TryGetChemicalStep(process, stepIndex, out SensitizationStep step))
            {
                return false;
            }

            string? requiredCodeString = step.RequiredPortionCode;
            if (string.IsNullOrWhiteSpace(requiredCodeString)) return false;

            Item? required = world.GetItem(new AssetLocation(requiredCodeString));
            if (required == null) return false;

            interaction = new WorldInteraction
            {
                ActionLangCode = actionLangCode,
                MouseButton = EnumMouseButton.Right,
                Itemstacks = new[] { new ItemStack(required, step.RequiredAmount) }
            };
            return true;
        }

        // Finalizes ground sensitization by giving the player the finished plate item and removing the placed block.
        private static bool GiveSensitizedPlateAndRemoveBlock(IWorldAccessor world, IServerPlayer sp, BlockPos pos, ItemStack sourcePlate, PhotographyProcessDefinition process)
        {
            if (!PlateSensitizationService.TryCreateSensitizedPlateStack(world, sourcePlate, process, out ItemStack sensitizedPlate))
            {
                return false;
            }

            if (!sp.InventoryManager.TryGiveItemstack(sensitizedPlate))
            {
                world.SpawnItemEntity(sensitizedPlate, pos.ToVec3d().Add(0.5, 0.5, 0.5));
            }

            world.BlockAccessor.SetBlock(0, pos);
            world.BlockAccessor.MarkBlockDirty(pos);
            return true;
        }
    }
}
