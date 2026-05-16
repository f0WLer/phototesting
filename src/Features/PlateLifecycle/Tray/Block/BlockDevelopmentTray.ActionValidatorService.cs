using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Phototesting.PlateLifecycle.Tray
{
    public sealed partial class BlockDevelopmentTray
    {
        // Dedicated validator collaborator for tray timed-action start/step checks.
        // Keeps state and chemical validation concerns separate from server apply logic.
        private static class ActionValidatorService
        {
            internal static bool TryValidateStartForAction(IWorldAccessor world, IPlayer byPlayer, BlockPos pos, BlockEntityDevelopmentTray be, ItemSlot? activeSlot, in TrayActionContext actionContext)
            {
                TrayDevelopmentSpec spec = actionContext.Spec;

                switch (actionContext.Kind)
                {
                    case TrayActionKind.Developer:
                        if (!TryGetDeveloperPourContext(be, spec.DeveloperApplicationsRequired, out ItemStack devPlate, out _, out _, out int currentPours)) return false;

                        if (currentPours >= spec.DeveloperApplicationsRequired) return false;
                        if (PlateDryingTransition.IsDry(world, devPlate))
                        {
                            Tell(byPlayer, "Wetplate: the plate has dried and can no longer be used.", pos);
                            return false;
                        }

                        break;
                    case TrayActionKind.Fixer:
                        if (!TryGetFixerPourContext(be, spec.DeveloperApplicationsRequired, out ItemStack fixPlate, out int pours)) return false;

                        if (PlateDryingTransition.IsDry(world, fixPlate))
                        {
                            Tell(byPlayer, "Wetplate: the plate has dried and can no longer be used.", pos);
                            return false;
                        }

                        if (pours < spec.DeveloperApplicationsRequired)
                        {
                            Tell(byPlayer, $"Wetplate: plate not fully developed ({pours}/{spec.DeveloperApplicationsRequired}).", pos);
                            return false;
                        }

                        break;
                    default:
                        if (!TryGetReclaimContext(be, world, out _)) return false;
                        break;
                }

                if (!WetPlateChemicalUtil.HasConsumableChemical(activeSlot, actionContext.PortionCode, actionContext.AmountPerUse))
                {
                    Tell(byPlayer, GetMissingChemicalMessage(actionContext.Kind), pos);
                    return false;
                }

                return true;
            }

            internal static bool TryValidateTimedStepForAction(IWorldAccessor world, IPlayer byPlayer, BlockPos pos, BlockEntityDevelopmentTray be, ItemSlot? activeSlot, in TrayActionContext actionContext, out ItemStack plate, out bool isExposed, out int currentApplications)
            {
                plate = null!;
                isExposed = false;
                currentApplications = 0;

                if (!IsHoldingChemical(activeSlot, actionContext.PortionCode)) return false;
                if (!WetPlateChemicalUtil.HasConsumableChemical(activeSlot, actionContext.PortionCode, actionContext.AmountPerUse))
                {
                    if (world.Side == EnumAppSide.Server)
                    {
                        Tell(byPlayer, GetMissingChemicalMessage(actionContext.Kind), pos);
                    }

                    return false;
                }

                TrayDevelopmentSpec spec = actionContext.Spec;
                switch (actionContext.Kind)
                {
                    case TrayActionKind.Developer:
                        if (!TryGetDeveloperPourContext(be, spec.DeveloperApplicationsRequired, out plate, out isExposed, out _, out currentApplications)) return false;
                        if (currentApplications >= spec.DeveloperApplicationsRequired) return false;
                        return true;
                    case TrayActionKind.Fixer:
                        if (!TryGetFixerPourContext(be, spec.DeveloperApplicationsRequired, out plate, out currentApplications)) return false;
                        if (currentApplications < spec.DeveloperApplicationsRequired)
                        {
                            if (world.Side == EnumAppSide.Server)
                            {
                                Tell(byPlayer, $"Wetplate: plate not fully developed ({currentApplications}/{spec.DeveloperApplicationsRequired}).", pos);
                            }

                            return false;
                        }

                        return true;
                    default:
                        return TryGetReclaimContext(be, world, out plate);
                }
            }
        }
    }
}
