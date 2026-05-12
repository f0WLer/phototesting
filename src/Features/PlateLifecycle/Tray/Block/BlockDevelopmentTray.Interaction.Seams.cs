using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Phototesting.PlateLifecycle.Tray
{
    // World-object interaction: shared runtime, helpers, and integration seams.
    public sealed partial class BlockDevelopmentTray
    {

        // Routes tray interaction start through side-aware world-object prediction/authority seams.
        private bool HandleWorldObjectInteractionStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            // Prevent immediately starting another timed action while RMB is still held.
            // This is enforced client-side only (server cannot observe mouse button state).
            if (world.Side == EnumAppSide.Client && NeedsRelease(byPlayer)) return false;

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

        // Keeps engine callback ownership local and delegates held-help composition to world-object seams.
        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
        {
            return BuildWorldObjectInteractionHelp(world, selection);
        }
    }
}
