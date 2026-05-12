using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Phototesting.PlateLifecycle;

namespace Phototesting.ImageEffects
{
    // One-time client-side process-effects profile seeding and validation warnings.
    internal static class ClientEffectProfileSeeder
    {
        // Seeds bundled defaults and emits missing-profile warnings once per session.
        internal static bool TryPrepare(
            ICoreClientAPI capi,
            ProcessRegistry processes,
            bool alreadyPrepared,
            ILogger? bestEffortLogger)
        {
            if (alreadyPrepared) return true;

            try
            {
                EnsureBundledProfilesInModData(capi, processes);
                WarnMissingProfiles(capi, processes);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(capi.Logger, "failed to prepare process effects profiles: {0}", ex.Message);
                Log.Warn(bestEffortLogger, "best-effort operation '{0}' failed: {1}: {2}", "prepare process effects profiles during assets load", ex.GetType().Name, ex.Message);
                return false;
            }
        }

        // Seeds missing ModData profiles from bundled assets so users get editable defaults on first run.
        private static void EnsureBundledProfilesInModData(ICoreClientAPI capi, ProcessRegistry processes)
        {
            foreach (PhotographyProcessDefinition process in processes.AllProcesses.Values)
            {
                string profileName = process.DefaultEffectsProfile?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(profileName)) continue;
                if (!ImageEffectsProfileService.IsValidProfileName(profileName)) continue;

                if (ImageEffectsProfileService.TrySeedNamedProfileFromBundledAsset(capi, profileName, out string modDataPath))
                {
                    Log.Notify(capi.Logger, "seeded default effects profile '{0}' for process '{1}' to '{2}'", profileName, process.Id, modDataPath);
                }
            }
        }

        // Warns once during profile preparation when a process references a missing/invalid profile file.
        private static void WarnMissingProfiles(ICoreClientAPI capi, ProcessRegistry processes)
        {
            foreach (PhotographyProcessDefinition process in processes.AllProcesses.Values)
            {
                string profileName = process.DefaultEffectsProfile?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(profileName))
                {
                    Log.Warn(capi.Logger, "process '{0}' has empty defaultEffectsProfile; capture falls back to baseline effects", process.Id);
                    continue;
                }

                if (!ImageEffectsProfileService.IsValidProfileName(profileName))
                {
                    Log.Warn(capi.Logger, "process '{0}' has invalid defaultEffectsProfile '{1}'; capture falls back to baseline effects", process.Id, profileName);
                    continue;
                }

                if (ImageEffectsProfileService.NamedProfileExists(profileName) || ImageEffectsProfileService.BundledProfileExists(capi, profileName)) continue;

                string path = ImageEffectsProfileService.GetNamedProfilePath(profileName);
                AssetLocation bundledPath = ImageEffectsProfileService.GetBundledProfileAssetLocation(profileName);
                Log.Warn(capi.Logger, "defaultEffectsProfile '{0}' for process '{1}' not found in ModData ('{2}') or bundled assets ('{3}'); capture falls back to baseline effects", profileName, process.Id, path, bundledPath);
            }
        }
    }
}