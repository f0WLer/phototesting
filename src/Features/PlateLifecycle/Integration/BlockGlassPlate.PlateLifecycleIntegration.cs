using Vintagestory.API.Common;

namespace Phototesting.PlateLifecycle.Blocks
{
    public sealed partial class BlockGlassPlate
    {
        // PlateLifecycle seam: routes glass-plate interaction start through a single entry orchestration point.
        private bool HandlePlateLifecycleInteractionStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            string state = GetPlateState();
            bool isRough = state == "rough";
            bool isSensitizable = state == "clean" || state == "coated";
            SensitizationStep nextStep = new("", null, 0, "");

            bool isPolish = isRough && IsPolishModifierDown(byPlayer) && IsHoldingPlainCloth(byPlayer);
            bool isSensitize = isSensitizable && CanStartGroundSensitization(byPlayer, blockSel.Position, state, out nextStep)
                               && nextStep.Kind == SensitizationStepKind.Chemical;
            bool isPickup = IsEmptyHand(byPlayer);

            if (world.Side == EnumAppSide.Client)
            {
                return isPolish || isSensitize || isPickup;
            }

            // Empty-hand pickup should always win so coated plates can be recovered at any point.
            if (isPickup)
            {
                GiveItemAndRemoveBlock(world, byPlayer, blockSel.Position);
                return true;
            }

            if (isPolish || isSensitize)
            {
                if (world.Side == EnumAppSide.Server)
                {
                    AssetLocation? sound = isPolish
                        ? _polishSound
                        : nextStep.Kind == SensitizationStepKind.Chemical ? _phototestingPourSound : null;
                    if (sound != null)
                    {
                        world.PlaySoundAt(sound, blockSel.Position.X + 0.5, blockSel.Position.Y + 0.5, blockSel.Position.Z + 0.5, null);
                    }
                }

                return true;
            }

            return false;
        }
    }
}

