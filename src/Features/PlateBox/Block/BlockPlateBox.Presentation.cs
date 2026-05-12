using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Phototesting.PlateBox
{
    public sealed partial class BlockPlateBox
    {
        // Provides held-help prompts for inserting and taking plates from slots.
        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
        {
            Item? samplePlate = world.GetItem(_samplePlateCode);
            if (samplePlate == null)
            {
                return base.GetPlacedBlockInteractionHelp(world, selection, forPlayer);
            }

            return new[]
            {
                new WorldInteraction
                {
                    ActionLangCode = "phototesting:heldhelp-platebox-insert",
                    MouseButton = EnumMouseButton.Right,
                    Itemstacks = new[] { new ItemStack(samplePlate) }
                },
                new WorldInteraction
                {
                    ActionLangCode = "phototesting:heldhelp-platebox-take",
                    MouseButton = EnumMouseButton.Right
                }
            };
        }
    }
}