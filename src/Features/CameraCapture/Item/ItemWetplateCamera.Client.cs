using Vintagestory.API.Common;
using Phototesting.AdminTooling;

namespace Phototesting.CameraCapture
{
    // Client held-interact loop for shutter input and accumulation exposure control.
    // Keeps LMB toggle triggering in sync with viewfinder state managed by PhotoTestingModSystem.
    public partial class ItemWetplateCamera
    {
        private static bool IsViewfinderActive(ICoreAPI api)
        {
            PhotoTestingModSystem? modSys = PhotoTestingConfigAccess.ResolveModSystem(api);
            return modSys != null && modSys.CameraCaptureBridge.IsViewfinderActive;
        }

        private static bool TryToggleExposure(ICoreAPI api, EntityAgent byEntity, bool silentIfBusy)
        {
            PhotoTestingModSystem? modSys = PhotoTestingConfigAccess.ResolveModSystem(api);
            if (modSys == null) return false;
            // Handheld camera always auto-halts once the target frame count is reached.
            return modSys.CameraCaptureBridge.TryToggleViewfinderExposure(byEntity, silentIfBusy, autoHaltAfterTarget: true);
        }

        // Drives shutter clicks while RMB viewfinder mode is active on the client.
        public override bool OnHeldInteractStep(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
        {
            if (api.Side != EnumAppSide.Client) return false;

            if (!IsViewfinderActive(api))
                return false;

            bool leftDown = byEntity.Controls?.LeftMouseDown == true;
            bool leftPrev = GetLmbPrev(byEntity);
            bool leftPressed = leftDown && !leftPrev;

            SetLmbPrev(byEntity, leftDown);

            if (leftPressed)
                TryToggleExposure(api, byEntity, silentIfBusy: true);

            // Keep the interact chain alive while RMB viewfinder is active.
            return true;
        }

        public override bool OnHeldInteractCancel(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, EnumItemUseCancelReason cancelReason)
        {
            if (api.Side == EnumAppSide.Client)
                SetLmbPrev(byEntity, false);

            return base.OnHeldInteractCancel(secondsUsed, slot, byEntity, blockSel, entitySel, cancelReason);
        }

        public override void OnHeldInteractStop(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
        {
            base.OnHeldInteractStop(secondsUsed, slot, byEntity, blockSel, entitySel);
            if (api.Side == EnumAppSide.Client)
                SetLmbPrev(byEntity, false);
        }
    }
}

