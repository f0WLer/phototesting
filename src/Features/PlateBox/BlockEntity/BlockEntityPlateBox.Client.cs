using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Phototesting.PlateLifecycle;

namespace Phototesting.PlateBox
{
    public sealed partial class BlockEntityPlateBox
    {
        internal const float SlotPlateWidth = 0.25f / 16f;
        internal const float SlotPlateHeight = 7.7f / 16f;
        internal const float SlotPlateDepth = 3.5f / 16f;
        private const float SlotPlateHeightScale = 0.5f;
        internal const float SlotPlateRenderHeight = SlotPlateHeight * SlotPlateHeightScale;
        internal const float SlotPlateTopAlignYOffset = SlotPlateHeight - SlotPlateRenderHeight;
        internal static readonly Vec3f SlotPlateOffset = new Vec3f(0.225f / 16f, 0f, 3.5f / 16f);

        internal static readonly Vec3f[] SlotOrigins =
        {
            // Matches platehb1..platehb8 "from" coordinates in platebox-open shape.
            new Vec3f(1.5f / 16f, 0.5f / 16f, 4.5f / 16f),
            new Vec3f(3.0f / 16f, 0.5f / 16f, 4.5f / 16f),
            new Vec3f(4.5f / 16f, 0.5f / 16f, 4.5f / 16f),
            new Vec3f(6.0f / 16f, 0.5f / 16f, 4.5f / 16f),
            new Vec3f(9.5f / 16f, 0.5f / 16f, 4.5f / 16f),
            new Vec3f(11.0f / 16f, 0.5f / 16f, 4.5f / 16f),
            new Vec3f(12.5f / 16f, 0.5f / 16f, 4.5f / 16f),
            new Vec3f(14.0f / 16f, 0.5f / 16f, 4.5f / 16f)
        };

        private PlateBoxSlotRenderer? _clientSlotRenderer;

        // Registers the per-slot renderer for this BlockEntity instance on the client.
        partial void ClientInitialize(ICoreAPI api)
        {
            _clientSlotRenderer = PlateBoxRenderLifecycle.EnsureRendererRegistered(api, this, _clientSlotRenderer);
            PlateBoxRenderLifecycle.TryMarkBlockDirty(api, Pos);
        }

        // Marks the block dirty client-side so slot visuals refresh after state changes.
        partial void ClientSlotsChanged(bool markBlockDirty)
        {
            if (!markBlockDirty) return;
            PlateBoxRenderLifecycle.TryMarkBlockDirty(Api, Pos);
        }

        // Disposes renderer resources when the block is removed.
        public override void OnBlockRemoved()
        {
            DisposeClientRenderer();
            base.OnBlockRemoved();
        }

        // Disposes renderer resources when the block entity unloads.
        public override void OnBlockUnloaded()
        {
            DisposeClientRenderer();
            base.OnBlockUnloaded();
        }

        // Unregisters and disposes the slot renderer safely.
        private void DisposeClientRenderer()
        {
            _clientSlotRenderer = PlateBoxRenderLifecycle.DisposeRenderer(Api, _clientSlotRenderer);
        }

        // Captures slot collectible code paths for render-thread-safe reads.
        internal string?[] SnapshotSlotCodePaths()
        {
            string?[] snapshot = new string?[SlotCount];

            lock (_slotLock)
            {
                for (int index = 0; index < SlotCount; index++)
                {
                    snapshot[index] = _plateSlots[index]?.Collectible?.Code?.Path;
                }
            }

            return snapshot;
        }

        // Captures slot plate stages for render-thread-safe tint selection.
        internal PlateStage[] SnapshotSlotStages()
        {
            PlateStage[] snapshot = new PlateStage[SlotCount];

            lock (_slotLock)
            {
                for (int index = 0; index < SlotCount; index++)
                {
                    snapshot[index] = PlateStateService.GetStage(_plateSlots[index]);
                }
            }

            return snapshot;
        }
    }
}

