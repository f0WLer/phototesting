using Vintagestory.API.Common;
using Phototesting.AdminTooling;

namespace Phototesting.CameraCapture
{
    // Client held-interact loop for shutter input and timed exposure lifetime.
    // Keeps capture triggering in sync with viewfinder state managed by PhotoTestingModSystem.
    public partial class ItemWetplateCamera
    {
        private static bool IsViewfinderActive(ICoreAPI api)
        {
            PhotoTestingModSystem? modSys = PhotoTestingConfigAccess.ResolveModSystem(api);
            return modSys != null && modSys.CameraCaptureBridge.IsViewfinderActive;
        }

        private static bool TryBeginCapture(ICoreAPI api, EntityAgent byEntity, bool silentIfBusy, out float exposureSeconds)
        {
            exposureSeconds = 0f;

            PhotoTestingModSystem? modSys = PhotoTestingConfigAccess.ResolveModSystem(api);
            if (modSys == null) return false;

            if (!modSys.CameraCaptureBridge.RequestPhotoCaptureFromViewfinder(byEntity, silentIfBusy)) return false;

            exposureSeconds = modSys.Config?.Viewfinder?.ExposureDurationSeconds ?? 0f;
            if (exposureSeconds < 0f) exposureSeconds = 0f;
            if (exposureSeconds > 30f) exposureSeconds = 30f;
            return true;
        }

        // Drives shutter clicks and timed exposure completion while RMB viewfinder mode is active on the client.
        public override bool OnHeldInteractStep(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
        {
            if (api.Side != EnumAppSide.Client) return false;

            if (!IsViewfinderActive(api))
            {
                ClearTimedExposure(byEntity);
                return false;
            }

            bool leftDown = byEntity.Controls?.LeftMouseDown == true;
            bool leftPrev = GetLmbPrev(byEntity);
            bool leftPressed = leftDown && !leftPrev;

            if (leftPressed && !IsTimedExposureActive(byEntity, out _))
            {
                if (TryBeginCapture(api, byEntity, silentIfBusy: true, out float startExposureSeconds))
                {
                    if (startExposureSeconds > 0f)
                    {
                        BeginTimedExposure(byEntity, startExposureSeconds);
                        AudioUtils.FireAndForgetEntitySound(api.World, _exposureStartSound, byEntity, AudioUtils.NextRandomPitch(api.World));
                    }
                }
            }

            SetLmbPrev(byEntity, leftDown);

            if (!IsTimedExposureActive(byEntity, out float exposureSeconds))
            {
                // Keep the interact chain alive while RMB viewfinder is active.
                return true;
            }

            float exposureElapsedSeconds = GetTimedExposureElapsedSeconds(byEntity);
            if (exposureElapsedSeconds < exposureSeconds)
            {
                // Keep interaction active while timed exposure is running.
                return true;
            }

            AudioUtils.FireAndForgetEntitySound(api.World, _exposureFinishSound, byEntity, AudioUtils.NextRandomPitch(api.World));
            ClearTimedExposure(byEntity);
            return true;
        }

        // Prevents the engine from cancelling the interaction early while a timed exposure is still running.
        public override bool OnHeldInteractCancel(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, EnumItemUseCancelReason cancelReason)
        {
            if (api.Side == EnumAppSide.Client)
            {
                if (IsTimedExposureActive(byEntity, out _))
                {
                    // Keep interact active until timed exposure finishes.
                    return false;
                }

                ClearTimedExposure(byEntity);
            }

            return base.OnHeldInteractCancel(secondsUsed, slot, byEntity, blockSel, entitySel, cancelReason);
        }

        // Resets temporary input state when the held interaction stops, unless a timed exposure still needs to finish.
        public override void OnHeldInteractStop(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
        {
            base.OnHeldInteractStop(secondsUsed, slot, byEntity, blockSel, entitySel);
            if (api.Side != EnumAppSide.Client) return;

            // Timed exposure may continue after input release; don't clear it here.
            if (IsTimedExposureActive(byEntity, out _))
            {
                return;
            }

            ClearTimedExposure(byEntity);

            // Viewfinder mode exit is driven by tick polling.
        }
    }
}


