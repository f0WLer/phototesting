using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Phototesting.PlateLifecycle.Tray
{
    public sealed partial class BlockDevelopmentTray
    {
        // Dedicated applier collaborator for authoritative server-side tray action outcomes.
        // Keeps mutation/application steps separate from validation and interaction lifecycle orchestration.
        private static class ActionApplierService
        {
            // Carries authoritative server apply output so plate write/swap logic stays unified.
            private readonly struct TrayActionApplyResult
            {
                internal TrayActionApplyResult(bool isSuccess, ItemStack? plate = null, string? stageVariantOverride = null, string? notificationMessage = null)
                {
                    IsSuccess = isSuccess;
                    Plate = plate;
                    StageVariantOverride = stageVariantOverride;
                    NotificationMessage = notificationMessage;
                }

                internal bool IsSuccess { get; }
                internal ItemStack? Plate { get; }
                internal string? StageVariantOverride { get; }
                internal string? NotificationMessage { get; }

                internal static TrayActionApplyResult Success(ItemStack plate, string? stageVariantOverride = null, string? notificationMessage = null)
                {
                    return new TrayActionApplyResult(true, plate, stageVariantOverride, notificationMessage);
                }

                internal static TrayActionApplyResult Failure(string? notificationMessage = null)
                {
                    return new TrayActionApplyResult(false, notificationMessage: notificationMessage);
                }
            }

            internal static bool TryApplyTimedActionServer(BlockDevelopmentTray owner, IWorldAccessor world, IPlayer byPlayer, BlockPos pos, BlockEntityDevelopmentTray be, ItemSlot? activeSlot, ItemStack plate, bool isExposed, int currentApplications, in TrayActionContext actionContext)
            {
                TrayActionApplyResult result = actionContext.Kind switch
                {
                    TrayActionKind.Developer => TryApplyDeveloperPourServer(owner, world, activeSlot, plate, isExposed, currentApplications, actionContext.Spec),
                    TrayActionKind.Fixer => TryApplyFixerPourServer(world, activeSlot, plate, actionContext.Spec),
                    _ => TryApplyWaterPourServer(world, activeSlot, actionContext.Spec)
                };

                return FinalizeActionResult(owner, world, byPlayer, pos, be, result);
            }

            private static TrayActionApplyResult TryApplyDeveloperPourServer(BlockDevelopmentTray owner, IWorldAccessor world, ItemSlot? activeSlot, ItemStack plate, bool isExposed, int currentPours, TrayDevelopmentSpec spec)
            {
                if (!WetPlateChemicalUtil.TryConsumeChemical(activeSlot, spec.DeveloperPortionCode, spec.DeveloperAmountPerUse))
                {
                    return TrayActionApplyResult.Failure(GetMissingChemicalMessage(TrayActionKind.Developer));
                }

                ItemStack newPlate = plate;
                if (isExposed)
                {
                    Item? developedItem = world.GetItem(_photoPlateItemCode);
                    if (developedItem == null) return TrayActionApplyResult.Failure();

                    newPlate = new ItemStack(developedItem);
                    try { newPlate.Attributes.MergeTree(plate.Attributes.Clone()); }
                    catch (Exception ex) { Log.Warn(world?.Logger, "TryApplyDeveloperPourServer: attribute merge failed: {0}", ex.Message); }
                }

                int newPours = currentPours + 1;
                if (newPours > spec.DeveloperApplicationsRequired) newPours = spec.DeveloperApplicationsRequired;

                PlateStateService.EnsureProcessId(newPlate);
                bool developerComplete = newPours >= spec.DeveloperApplicationsRequired;
                PlateStateService.SetStage(newPlate, developerComplete ? PlateStage.Developed : PlateStage.Developing);

                if (developerComplete)
                {
                    PlateDevelopmentService.SetDevelopmentStepIndex(newPlate, spec.DeveloperStepIndex);
                    PlateDevelopmentService.SetCurrentStepApplications(newPlate, 0);
                }
                else
                {
                    PlateDevelopmentService.SetCurrentStepApplications(newPlate, newPours);
                }

                PlateLifecycleStateCoordinator.ResetWetTimerForMultiplier(owner.api, world!, newPlate, spec.WetDurationMultiplier);
                return TrayActionApplyResult.Success(newPlate);
            }

            private static TrayActionApplyResult TryApplyFixerPourServer(IWorldAccessor world, ItemSlot? activeSlot, ItemStack plate, TrayDevelopmentSpec spec)
            {
                if (!WetPlateChemicalUtil.TryConsumeChemical(activeSlot, spec.FixerPortionCode, spec.FixerAmountPerUse))
                {
                    return TrayActionApplyResult.Failure(GetMissingChemicalMessage(TrayActionKind.Fixer));
                }

                int currentFixerApplications = PlateDevelopmentService.GetCurrentStepApplications(plate);
                int newFixerApplications = currentFixerApplications + 1;
                bool fixerComplete = newFixerApplications >= spec.FixerApplicationsRequired;

                ItemStack newPlate;
                if (fixerComplete)
                {
                    Item? finishedItem = world.GetItem(_photoPlateItemCode);
                    if (finishedItem == null) return TrayActionApplyResult.Failure();

                    newPlate = new ItemStack(finishedItem);
                    try { newPlate.Attributes.MergeTree(plate.Attributes.Clone()); }
                    catch (Exception ex) { Log.Warn(world?.Logger, "TryApplyFixerPourServer: attribute merge failed: {0}", ex.Message); }

                    PlateStateService.EnsureProcessId(newPlate);
                    PlateStateService.SetStage(newPlate, PlateStage.Finished);
                    PlateDevelopmentService.SetDevelopmentStepIndex(newPlate, spec.FixerStepIndex);
                    PlateDevelopmentService.SetCurrentStepApplications(newPlate, 0);
                }
                else
                {
                    newPlate = plate.Clone();
                    PlateStateService.EnsureProcessId(newPlate);
                    PlateStateService.SetStage(newPlate, PlateStage.Developed);
                    PlateDevelopmentService.SetCurrentStepApplications(newPlate, newFixerApplications);
                }

                return TrayActionApplyResult.Success(newPlate);
            }

            private static TrayActionApplyResult TryApplyWaterPourServer(IWorldAccessor world, ItemSlot? activeSlot, TrayDevelopmentSpec spec)
            {
                if (!WetPlateChemicalUtil.TryConsumeChemical(activeSlot, spec.WaterPortionCode, spec.WaterAmountPerUse))
                {
                    return TrayActionApplyResult.Failure(GetMissingChemicalMessage(TrayActionKind.Water));
                }

                Item? glassPlateItem = world.GetItem(_glassPlateItemCode);
                if (glassPlateItem == null) return TrayActionApplyResult.Failure();

                ItemStack reclaimedPlate = new ItemStack(glassPlateItem);
                PlateLifecycleStateCoordinator.TransitionToRough(reclaimedPlate, "phototesting:plate-name-glass");
                reclaimedPlate.Attributes.SetString("plateBlockState", "rough");
                PlateDevelopmentService.ResetDevelopmentProgress(reclaimedPlate);
                return TrayActionApplyResult.Success(reclaimedPlate, stageVariantOverride: "reclaimed");
            }

            // Finalizes timed-action results in one place so notification and tray mutation stay aligned.
            private static bool FinalizeActionResult(BlockDevelopmentTray owner, IWorldAccessor world, IPlayer byPlayer, BlockPos pos, BlockEntityDevelopmentTray be, in TrayActionApplyResult result)
            {
                if (!string.IsNullOrWhiteSpace(result.NotificationMessage))
                {
                    Tell(byPlayer, result.NotificationMessage, pos);
                }

                if (!result.IsSuccess || result.Plate == null)
                {
                    return false;
                }

                be.TrySetPlate(result.Plate);

                string? stageVariant = result.StageVariantOverride
                    ?? PlateStageUtil.ToAttributeString(PlateStateService.GetStage(result.Plate));

                owner.SwapTrayBlockForPlateStage(world, pos, stageVariant, result.Plate);
                return true;
            }
        }
    }
}
