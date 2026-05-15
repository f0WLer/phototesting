using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Phototesting.PlateLifecycle.GroundPlate
{
    public sealed partial class BlockGlassPlate
    {
        // Routes placed-plate RMB interaction into polish, sensitization, or pickup behavior based on plate state and held item.
        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (world == null || byPlayer == null || blockSel == null) return false;
            return HandlePlateLifecycleInteractionStart(world, byPlayer, blockSel);
        }

        // Advances the timed polish or sensitization action once the required hold duration has elapsed.
        public override bool OnBlockInteractStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (world == null || byPlayer == null || blockSel == null) return false;

            string state = GetPlateState();
            if (state == "rough")
            {
                return HandlePolishInteraction(secondsUsed, world, byPlayer, blockSel.Position);
            }

            bool isGroundSensitize = state == "clean" || state == "coated";
            if (!isGroundSensitize) return false;
            return HandleGroundSensitizationInteractionStep(secondsUsed, world, byPlayer, blockSel.Position, state);
        }

        // Builds the held-help entry that matches the next valid plate interaction for the current state.
        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
        {
            string state = GetPlateState();
            if (state == "rough")
            {
                return BuildPolishInteractionHelp(world, selection, forPlayer);
            }

            if ((state == "clean" || state == "coated") && TryGetGroundSensitizationHelp(world, selection?.Position, state, forPlayer, out WorldInteraction interaction))
            {
                return [interaction];
            }

            return base.GetPlacedBlockInteractionHelp(world, selection, forPlayer);
        }

    }
}
