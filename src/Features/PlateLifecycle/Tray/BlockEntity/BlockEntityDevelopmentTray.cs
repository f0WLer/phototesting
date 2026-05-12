using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Phototesting.PlateLifecycle.Tray
{
    // Development-tray block entity. Holds the placed plate stack + facing,
    // mediates client-side mesh refresh through partial hooks (ClientInitialize/ClientPlateChanged),
    // and serializes plate signature for change detection. Server logic lives in tray Block partials.
    public sealed partial class BlockEntityDevelopmentTray : BlockEntity
    {
        private const string AttrPlateStack = "phototestingPlateStack";
        private const string AttrPlacementFacing = "phototestingPlacementFacing";

        private readonly object _plateLock = new();
        private string? _lastPlateSignature;

        public ItemStack? PlateStack { get; private set; }
        public string PlacementFacingCode { get; private set; } = "east";

        partial void ClientInitialize(ICoreAPI api);
        partial void ClientPlateChanged(bool markBlockDirty);

        public bool HasPlate
        {
            get
            {
                lock (_plateLock)
                {
                    return PlateStack != null;
                }
            }
        }

        // Replays client plate-change handling after disk load so the tray rebuilds presentation from persisted state.
        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            // If we already have a plate (loaded from disk), ensure the chunk is rebuilt so it shows.
            ClientPlateChanged(markBlockDirty: true);

            ClientInitialize(api);
        }

        // Inserts a single cloned plate into the tray's authoritative block-entity state.
        public bool TryInsertPlate(ItemStack stack)
        {
            if (stack == null) return false;
            if (HasPlate) return false;

            lock (_plateLock)
            {
                PlateStack = stack.Clone();
                PlateStack.StackSize = 1;
                _lastPlateSignature = ComputePlateSignature(PlateStack);
            }

            ClientPlateChanged(markBlockDirty: true);
            MarkDirty(true);
            return true;
        }

        // Removes and returns the plate currently stored in the tray.
        public ItemStack? TakePlate()
        {
            if (!HasPlate) return null;

            ItemStack? stack;
            lock (_plateLock)
            {
                stack = PlateStack;
                PlateStack = null;
                _lastPlateSignature = null;
            }

            ClientPlateChanged(markBlockDirty: true);
            MarkDirty(true);
            return stack;
        }

        // Replaces the tray's stored plate with the supplied stack, preserving the one-item invariant.
        public bool TrySetPlate(ItemStack stack)
        {
            if (stack == null) return false;

            lock (_plateLock)
            {
                PlateStack = stack;
                PlateStack.StackSize = 1;
                _lastPlateSignature = ComputePlateSignature(PlateStack);
            }

            ClientPlateChanged(markBlockDirty: true);
            MarkDirty(true);
            return true;
        }

        // Persists the tray-facing used by client rendering when plate and photo meshes are rebuilt.
        public bool SetPlacementFacing(string? facingCode, bool markBlockDirty = true)
        {
            string normalized = NormalizeHorizontalFacingCode(facingCode);
            if (string.Equals(PlacementFacingCode, normalized, StringComparison.Ordinal)) return false;

            PlacementFacingCode = normalized;
            ClientPlateChanged(markBlockDirty);
            MarkDirty(true);
            return true;
        }

        // Restores the saved plate stack and facing from tree attributes and triggers a client rebuild when they change.
        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);

            try
            {
                ItemStack? loaded = tree.GetItemstack(AttrPlateStack, null);
                loaded?.ResolveBlockOrItem(worldAccessForResolve);

                bool changed;
                string? newSig = ComputePlateSignature(loaded);
                string newFacing = NormalizeHorizontalFacingCode(tree.GetString(AttrPlacementFacing, "east"));
                lock (_plateLock)
                {
                    changed = (PlateStack == null) != (loaded == null)
                        || (PlateStack?.Collectible?.Code != loaded?.Collectible?.Code)
                        || !string.Equals(_lastPlateSignature, newSig, StringComparison.Ordinal)
                        || !string.Equals(PlacementFacingCode, newFacing, StringComparison.Ordinal);
                    PlateStack = loaded;
                    _lastPlateSignature = newSig;
                    PlacementFacingCode = newFacing;
                }

                if (changed) ClientPlateChanged(markBlockDirty: true);
            }
            catch
            {
                lock (_plateLock)
                {
                    PlateStack = null;
                    _lastPlateSignature = null;
                    PlacementFacingCode = "east";
                }

                ClientPlateChanged(markBlockDirty: true);
            }
        }

        // Serializes the tray's stored plate and facing so stage swaps and chunk saves can recreate the same tray state.
        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);

            ItemStack? toSave;
            lock (_plateLock)
            {
                toSave = PlateStack;
            }

            if (toSave != null) tree.SetItemstack(AttrPlateStack, toSave);
            else tree.RemoveAttribute(AttrPlateStack);

            tree.SetString(AttrPlacementFacing, NormalizeHorizontalFacingCode(PlacementFacingCode));
        }

        // Exposes a small debug summary of the tray's current plate for block info overlays.
        public override void GetBlockInfo(IPlayer forPlayer, System.Text.StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);

            ItemStack? plate;
            lock (_plateLock)
            {
                plate = PlateStack;
            }

            if (plate?.Collectible?.Code != null)
            {
                dsc.AppendLine($"Plate: {plate.Collectible.Code}");
            }
            else
            {
                dsc.AppendLine("Plate: (none)");
            }
        }

        // Normalizes any facing input down to the horizontal codes the tray renderer understands.
        private static string NormalizeHorizontalFacingCode(string? facingCode)
        {
            return facingCode?.ToLowerInvariant() switch
            {
                "north" => "north",
                "south" => "south",
                "west" => "west",
                _ => "east"
            };
        }

        // Produces a compact signature for the current plate so client render caches can detect meaningful visual changes.
        private static string? ComputePlateSignature(ItemStack? stack)
        {
            if (stack?.Collectible?.Code == null) return null;

            string code = stack.Collectible.Code.ToString();
            string photoId = stack.Attributes?.GetString(WetPlateAttrs.PhotoId) ?? string.Empty;
            string stage = PlateStageUtil.ToAttributeString(PlateStateService.GetStage(stack));
            int pours = PlateDevelopmentService.GetCurrentStepApplications(stack);

            return $"{code}|{photoId}|{stage}|{pours}";
        }
    }
}

