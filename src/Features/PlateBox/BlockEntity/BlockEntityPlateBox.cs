using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Phototesting.PlateLifecycle;

namespace Phototesting.PlateBox
{
    public sealed partial class BlockEntityPlateBox : BlockEntity
    {
        private const string BlockSlotPrefix = "phototestingPlateBoxSlot";
        private const string ItemSlotPrefix = "phototestingPlateBoxItemSlot";
        private const string BlockOpenAttr = "phototestingPlateBoxOpen";

        public const int SlotCount = 8;

        private readonly object _slotLock = new();
        private readonly ItemStack?[] _plateSlots = new ItemStack?[SlotCount];
        private bool _isOpen;

        // Client-side renderer/bootstrap hook implemented in the .Client partial.
        partial void ClientInitialize(ICoreAPI api);
        // Client-side dirty/render invalidation hook implemented in the .Client partial.
        partial void ClientSlotsChanged(bool markBlockDirty);

        // Initializes base state and client renderer lifecycle hooks.
        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            ClientInitialize(api);
        }

        // Returns whether a specific slot currently contains a plate stack.
        public bool HasPlateAt(int slotIndex)
        {
            if ((uint)slotIndex >= SlotCount) return false;

            lock (_slotLock)
            {
                return _plateSlots[slotIndex] != null;
            }
        }

        // Exposes whether the current runtime variant should be treated as open.
        public bool IsOpen
        {
            get
            {
                lock (_slotLock)
                {
                    return _isOpen;
                }
            }
        }

        // Updates open state and triggers a synchronized state-change notification when needed.
        internal bool SetOpen(bool open)
        {
            bool changed;

            lock (_slotLock)
            {
                changed = _isOpen != open;
                _isOpen = open;
            }

            if (changed)
            {
                OnSlotsChanged();
            }

            return changed;
        }

        // Checks whether a target slot can accept a new plate.
        internal bool CanInsertAt(int slotIndex)
        {
            if ((uint)slotIndex >= SlotCount) return false;

            lock (_slotLock)
            {
                return _plateSlots[slotIndex] == null;
            }
        }

        // Counts non-empty slots for tooltip and debug reporting.
        private int GetUsedSlotCount()
        {
            int count = 0;

            lock (_slotLock)
            {
                for (int index = 0; index < SlotCount; index++)
                {
                    if (_plateSlots[index] != null) count++;
                }
            }

            return count;
        }

        // Inserts one plate into a slot and pauses wetness decay while stored.
        internal bool TryInsertPlateAt(int slotIndex, ItemStack stack, IWorldAccessor world)
        {
            if ((uint)slotIndex >= SlotCount || stack == null || !IsInsertablePlate(stack)) return false;

            ItemStack insertStack = stack.Clone();
            insertStack.StackSize = 1;
            WetPlateAttrs.PauseWetTimerForStorage(world, insertStack);

            lock (_slotLock)
            {
                if (_plateSlots[slotIndex] != null) return false;
                _plateSlots[slotIndex] = insertStack;
            }

            OnSlotsChanged();
            return true;
        }

        // Removes one stored plate from a slot and resumes wetness decay.
        internal ItemStack? TakePlateAt(int slotIndex, IWorldAccessor world)
        {
            if ((uint)slotIndex >= SlotCount) return null;

            ItemStack? stack;
            lock (_slotLock)
            {
                stack = _plateSlots[slotIndex];
                if (stack == null) return null;
                _plateSlots[slotIndex] = null;
            }

            ItemStack output = stack.Clone();
            WetPlateAttrs.ResumeWetTimerFromStorage(world, output);

            OnSlotsChanged();
            return output;
        }

        // Serializes slots and open-state into block entity tree attributes.
        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            WriteSlotsToAttributes(tree, BlockSlotPrefix);
            tree.SetBool(BlockOpenAttr, IsOpen);
        }

        // Deserializes slots and open-state from block entity tree attributes.
        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);

            lock (_slotLock)
            {
                _isOpen = tree.GetBool(BlockOpenAttr, false);
            }

            ReadSlotsFromAttributes(tree, BlockSlotPrefix, worldAccessForResolve);
        }

        // Saves slot contents to an item payload for drop/pick/place persistence.
        internal void SaveToItemStack(ItemStack target)
        {
            if (target?.Attributes == null) return;
            WriteSlotsToAttributes(target.Attributes, ItemSlotPrefix);
        }

        // Loads slot contents from an item payload after placement.
        internal void LoadFromItemStack(ItemStack source, IWorldAccessor world)
        {
            if (source?.Attributes == null) return;
            ReadSlotsFromAttributes(source.Attributes, ItemSlotPrefix, world);
        }

        // Appends compact plate-slot usage information to block inspection text.
        public override void GetBlockInfo(IPlayer forPlayer, System.Text.StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);
            dsc.AppendLine($"Plate slots: {GetUsedSlotCount()}/{SlotCount}");
        }

        // Accepts only plate item stacks as storable content.
        public static bool IsInsertablePlate(ItemStack? stack)
        {
            return stack?.Item is ItemPlateBase;
        }

        // Writes each slot stack using the provided key prefix.
        private void WriteSlotsToAttributes(ITreeAttribute attrs, string prefix)
        {
            lock (_slotLock)
            {
                for (int index = 0; index < SlotCount; index++)
                {
                    string key = prefix + index;
                    ItemStack? slot = _plateSlots[index];
                    if (slot != null) attrs.SetItemstack(key, slot);
                    else attrs.RemoveAttribute(key);
                }
            }
        }

        // Reads, resolves, and sanitizes slot stacks from prefixed attribute keys.
        private void ReadSlotsFromAttributes(ITreeAttribute attrs, string prefix, IWorldAccessor world)
        {
            lock (_slotLock)
            {
                for (int index = 0; index < SlotCount; index++)
                {
                    string key = prefix + index;
                    ItemStack? loaded = attrs.TryGetAttribute(key, out IAttribute raw) && raw is ItemstackAttribute isa ? isa.value : null;
                    loaded?.ResolveBlockOrItem(world);
                    if (loaded != null && !IsInsertablePlate(loaded)) loaded = null;
                    if (loaded != null) loaded.StackSize = 1;
                    _plateSlots[index] = loaded;
                }
            }

            OnSlotsChanged();
        }

        // Centralizes synchronization after any slot/open-state mutation.
        private void OnSlotsChanged()
        {
            ClientSlotsChanged(markBlockDirty: true);
            MarkDirty(true);
        }
    }
}
