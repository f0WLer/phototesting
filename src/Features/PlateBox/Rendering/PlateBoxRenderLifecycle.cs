using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Phototesting.PlateBox
{
    internal static class PlateBoxRenderLifecycle
    {
        // Registers the client renderer once per BE instance and returns the active renderer reference.
        internal static PlateBoxSlotRenderer? EnsureRendererRegistered(ICoreAPI api, BlockEntityPlateBox owner, PlateBoxSlotRenderer? renderer)
        {
            if (api?.Side != EnumAppSide.Client) return renderer;
            if (renderer != null) return renderer;

            ICoreClientAPI capi = (ICoreClientAPI)api;
            renderer = new PlateBoxSlotRenderer(capi, owner);
            capi.Event.RegisterRenderer(renderer, EnumRenderStage.Opaque, "phototesting-platebox-slotrender");
            return renderer;
        }

        // Marks the block dirty client-side to trigger chunk retesselation.
        internal static void TryMarkBlockDirty(ICoreAPI? api, BlockPos pos)
        {
            if (api?.Side != EnumAppSide.Client) return;

            try
            {
                ((ICoreClientAPI)api).World.BlockAccessor.MarkBlockDirty(pos);
            }
            catch
            {
                // ignore
            }
        }

        // Unregisters and disposes renderer resources when BE lifecycle ends.
        internal static PlateBoxSlotRenderer? DisposeRenderer(ICoreAPI? api, PlateBoxSlotRenderer? renderer)
        {
            if (api?.Side != EnumAppSide.Client || renderer == null) return renderer;

            try
            {
                ICoreClientAPI capi = (ICoreClientAPI)api;
                capi.Event.UnregisterRenderer(renderer, EnumRenderStage.Opaque);
                renderer.Dispose();
            }
            catch
            {
                // ignore
            }

            return null;
        }
    }
}