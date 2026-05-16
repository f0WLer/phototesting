using Phototesting.PlateLifecycle;
using Vintagestory.API.Common;

namespace Phototesting.CameraCapture
{
    // Client-side viewfinder: state machine, hold-still coordinator, capture gate, effects profile, and zoom harmony patch.
    internal sealed partial class CameraCaptureModSystemBridge
    {

        // Dedicated gate collaborator for validating whether a capture request is currently allowed.
        internal static class CaptureGateService
        {
            internal static bool TryValidateCaptureRequest(CameraCaptureModSystemBridge owner, bool silentIfBusy, bool isMounted, out ItemStack? loadedPlateStack)
            {
                loadedPlateStack = null;

                if (owner.ClientApi == null || owner.ClientChannel == null) return false;
                if (!isMounted && owner._captureRenderer == null) return false;

                // Prevent "late shutter" after RMB release.
                if (!owner.CaptureClientRuntime.GetRightMouseDown()) return false;

                // Shutter gating: You can only take a photo when a sensitized plate is loaded.
                // You should always be able to zoom, so we gate only capture (not BeginViewfinderMode).
                try
                {
                    ItemStack? camStack = CameraItemHelper.GetActiveCameraStack(owner.ClientApi);
                    if (camStack == null) return false;

                    if (!CameraPlateEligibility.IsLoadedCodeSensitized(camStack.Attributes.GetString(ItemWetplateCamera.AttrLoadedPlate, string.Empty)))
                    {
                        owner.CaptureClientRuntime.ShowShutterGateMessageThrottled("Wetplate: load a sensitized plate to take a photo.");
                        return false;
                    }

                    // Sensitized plate loaded - verify stage and wetness.
                    CameraItemHelper.TryGetLoadedPlateStack(
                        camStack,
                        owner.ClientApi.World,
                        out loadedPlateStack,
                        ex => Log.Debug(owner.ClientApi.Logger, "viewfinder loaded plate resolve failed: {0}", ex.Message));

                    // Keep capture gate permissive when only the lightweight loaded-code attribute exists.
                    if (loadedPlateStack != null && !CameraPlateEligibility.IsPlateSensitizedForExposure(loadedPlateStack))
                    {
                        owner.CaptureClientRuntime.ShowShutterGateMessageThrottled("Wetplate: only sensitized plates can be exposed.");
                        return false;
                    }

                    if (loadedPlateStack != null && PlateDryingTransition.IsDry(owner.ClientApi.World, loadedPlateStack))
                    {
                        owner.CaptureClientRuntime.ShowShutterGateMessageThrottled("Wetplate: the plate has dried and can no longer be used.");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    if (owner.IsBestEffortDebugLoggingEnabled) Log.Warn(owner.ClientApi.Logger, "capture request validation failed: {0}", ex.Message);
                    return false;
                }

                if (owner.IsHoldStillPending && !isMounted)
                {
                    if (!silentIfBusy) owner.ClientApi.ShowChatMessage("Wetplate: hold still to finish the exposure.");
                    return false;
                }

                return true;
            }
        }
    }
}
