using Phototesting.ImageEffects;
using Phototesting.PlateLifecycle;
using Vintagestory.API.Common;

namespace Phototesting.CameraCapture
{
    // Client-side viewfinder: state machine, hold-still coordinator, capture gate, effects profile, and zoom harmony patch.
    internal sealed partial class CameraCaptureModSystemBridge
    {

        // Dedicated resolver collaborator for selecting capture effects profile from loaded plate process metadata.
        internal static class CaptureEffectsProfileLookup
        {
            // Resolves the capture profile from the processId attached to the loaded plate stack.
            // Returns null to keep the renderer on its baseline effects config.
            internal static WetplateEffectsConfig? ResolveForLoadedPlate(CameraCaptureModSystemBridge owner, ItemStack? loadedPlateStack)
            {
                if (owner.ClientApi == null || loadedPlateStack == null) return null;

                string? attachedProcessId = loadedPlateStack.Attributes?.GetString(PlateStateAttributes.ProcessId);
                if (string.IsNullOrWhiteSpace(attachedProcessId)) return null;

                string processId = attachedProcessId.Trim();
                if (!owner.Processes.AllProcesses.TryGetValue(processId, out PhotographyProcessDefinition? processDef) || processDef == null)
                {
                    if (owner.IsBestEffortDebugLoggingEnabled) Log.Warn(owner.ClientApi.Logger, "capture effects: unknown loaded plate processId '{0}', using baseline profile", processId);
                    return null;
                }

                string profileName = processDef.DefaultEffectsProfile?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(profileName))
                {
                    if (owner.IsBestEffortDebugLoggingEnabled) Log.Warn(owner.ClientApi.Logger, "capture effects: process '{0}' has empty defaultEffectsProfile, using baseline profile", processDef.Id);
                    return null;
                }

                WetplateEffectsConfig? overrideCfg = ImageEffectsProfileService.TryLoadNamedProfile(profileName, owner.ClientApi);
                if (overrideCfg != null) return overrideCfg;

                if (owner.IsBestEffortDebugLoggingEnabled)
                {
                    string path = ImageEffectsProfileService.GetNamedProfilePath(profileName);
                    Log.Warn(owner.ClientApi.Logger, "capture effects: profile '{0}' for process '{1}' missing/invalid at '{2}', using baseline profile", profileName, processDef.Id, path);
                }

                return null;
            }

            // Resolves capture effects override for the currently loaded camera plate, if present.
            // Returns null to keep renderer on baseline profile when no loaded stack/process is available.
            internal static WetplateEffectsConfig? ResolveForLoadedCamera(CameraCaptureModSystemBridge owner)
            {
                if (owner.ClientApi == null) return null;

                ItemStack? camStack = CameraItemHelper.GetActiveCameraStack(owner.ClientApi);
                if (camStack == null) return null;

                CameraItemHelper.TryGetLoadedPlateStack(
                    camStack,
                    owner.ClientApi.World,
                    out ItemStack? loadedPlateStack,
                    // Preview should remain best-effort even when loaded-stack rehydration fails.
                    ex => { if (owner.IsBestEffortDebugLoggingEnabled) Log.Warn(owner.ClientApi.Logger, "capture effects: failed to resolve loaded plate stack for preview: {0}", ex.Message); });

                return ResolveForLoadedPlate(owner, loadedPlateStack);
            }
        }
    }
}
