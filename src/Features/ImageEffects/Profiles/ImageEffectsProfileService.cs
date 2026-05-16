using Newtonsoft.Json;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Phototesting.AdminTooling;

namespace Phototesting.ImageEffects
{
    // Named effects profile lookup, fallback, and runtime snapshot behavior.
    internal static class ImageEffectsProfileService
    {
        // Gets bundled default profile location inside mod assets.
        internal static AssetLocation GetBundledProfileAssetLocation(string profileName)
        {
            string trimmed = (profileName ?? string.Empty).Trim().ToLowerInvariant();
            return new AssetLocation("phototesting", $"config/effects/{trimmed}.json");
        }

        // Validates profile names used by load/save/fallback profile flows.
        internal static bool IsValidProfileName(string profileName)
        {
            return WetplateEffectsProfileStore.IsValidProfileName(profileName);
        }

        // Gets the absolute ModData path for a named wetplate profile.
        internal static string GetNamedProfilePath(string profileName)
        {
            string trimmed = (profileName ?? string.Empty).Trim();
            return WetplateEffectsProfileStore.GetProfilePath(trimmed);
        }

        // Persists a validated named profile to ModData.
        internal static void SaveNamedProfile(string profileName, WetplateEffectsConfig cfg)
        {
            string trimmed = (profileName ?? string.Empty).Trim();
            if (!IsValidProfileName(trimmed)) throw new ArgumentException("Invalid effects profile name.", nameof(profileName));

            WetplateEffectsConfig toSave = cfg?.Clone() ?? throw new ArgumentNullException(nameof(cfg));
            toSave.ClampInPlace();
            WetplateEffectsProfileStore.SaveProfile(trimmed, toSave);
        }

        // Returns true when a safe, named profile file exists on disk.
        internal static bool NamedProfileExists(string profileName)
        {
            if (string.IsNullOrWhiteSpace(profileName)) return false;

            string trimmed = profileName.Trim();
            if (!IsValidProfileName(trimmed)) return false;

            return File.Exists(WetplateEffectsProfileStore.GetProfilePath(trimmed));
        }

        // Returns true when a safe, named bundled profile exists in mod assets.
        internal static bool BundledProfileExists(ICoreClientAPI capi, string profileName)
        {
            if (capi == null) return false;
            if (string.IsNullOrWhiteSpace(profileName)) return false;

            string trimmed = profileName.Trim();
            if (!IsValidProfileName(trimmed)) return false;

            AssetLocation jsonLocation = GetBundledProfileAssetLocation(trimmed);
            return capi.Assets.TryGet(jsonLocation, loadAsset: true) != null;
        }

        // Copies bundled profile into ModData when no user override exists.
        internal static bool TrySeedNamedProfileFromBundledAsset(ICoreClientAPI capi, string profileName, out string modDataPath)
        {
            modDataPath = string.Empty;
            if (capi == null) return false;
            if (string.IsNullOrWhiteSpace(profileName)) return false;

            string trimmed = profileName.Trim();
            if (!IsValidProfileName(trimmed)) return false;
            if (NamedProfileExists(trimmed)) return false;

            WetplateEffectsConfig? bundledCfg = TryLoadBundledNamedProfile(capi, trimmed);
            if (bundledCfg == null) return false;

            try
            {
                WetplateEffectsProfileStore.SaveProfile(trimmed, bundledCfg);
                modDataPath = WetplateEffectsProfileStore.GetProfilePath(trimmed);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(capi.Logger, "failed to seed ModData effects profile '{0}' from bundled assets: {1}", trimmed, ex.Message);
                return false;
            }
        }

        // Loads bundled profile from mod assets.
        private static WetplateEffectsConfig? TryLoadBundledNamedProfile(ICoreClientAPI capi, string profileName)
        {
            if (capi == null || string.IsNullOrWhiteSpace(profileName)) return null;
            if (!IsValidProfileName(profileName)) return null;

            try
            {
                AssetLocation jsonLocation = GetBundledProfileAssetLocation(profileName);
                IAsset? asset = capi.Assets.TryGet(jsonLocation, loadAsset: true);

                if (asset == null) return null;

                WetplateEffectsConfig? cfg = JsonConvert.DeserializeObject<WetplateEffectsConfig>(asset.ToText());
                cfg?.ClampInPlace();
                return cfg;
            }
            catch (Exception ex)
            {
                Log.Warn(capi.Logger, "failed to parse bundled effects profile '{0}' at '{1}': {2}", profileName, GetBundledProfileAssetLocation(profileName), ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Loads a named effects profile from ModData first, then bundled mod assets.
        /// Returns null when no valid profile is found.
        /// </summary>
        internal static WetplateEffectsConfig? TryLoadNamedProfile(string profileName, ICoreClientAPI? capi = null)
        {
            if (string.IsNullOrWhiteSpace(profileName)) return null;

            string trimmed = profileName.Trim();
            if (!IsValidProfileName(trimmed)) return null;

            string path = WetplateEffectsProfileStore.GetProfilePath(trimmed);
            if (File.Exists(path))
            {
                try
                {
                    WetplateEffectsConfig? cfg = JsonConvert.DeserializeObject<WetplateEffectsConfig>(File.ReadAllText(path));
                    cfg?.ClampInPlace();
                    return cfg;
                }
                catch (Exception ex)
                {
                    Log.Warn(capi?.Logger, "failed to parse ModData effects profile '{0}' at '{1}': {2}; falling back to bundled profile", trimmed, path, ex.Message);
                    // Fall back to bundled profile below.
                }
            }

            if (capi == null) return null;

            WetplateEffectsConfig? bundledCfg = TryLoadBundledNamedProfile(capi, trimmed);
            if (bundledCfg == null) return null;

            // Self-heal missing ModData profile files when bundled defaults are used at runtime.
            if (!NamedProfileExists(trimmed))
            {
                try
                {
                    WetplateEffectsProfileStore.SaveProfile(trimmed, bundledCfg);
                }
                catch (Exception ex)
                {
                    Log.Warn(capi.Logger, "failed to write self-healed ModData effects profile '{0}': {1}", trimmed, ex.Message);
                }
            }

            return bundledCfg;
        }

        // Loads active config from mod system, ensuring an effects profile exists and is clamped.
        internal static WetplateEffectsConfig LoadOrCreate(ICoreClientAPI capi)
        {
            PhotoTestingModSystem? modSys = PhotoTestingConfigAccess.ResolveModSystem(capi);
            if (modSys == null)
            {
                return CreateRuntimeSnapshot(null);
            }

            PhotoTestingConfig cfg = modSys.GetOrLoadClientConfig(capi);
            bool dirty = false;

            if (cfg.Effects == null)
            {
                cfg.Effects = new WetplateEffectsConfig();
                dirty = true;
            }

            cfg.Effects.ClampInPlace();

            if (dirty)
            {
                modSys.SaveClientConfig(capi);
            }

            return CreateRuntimeSnapshot(cfg.Effects);
        }

        // Clones and clamps an effects profile for runtime use so hot paths can treat it as immutable.
        internal static WetplateEffectsConfig CreateRuntimeSnapshot(WetplateEffectsConfig? cfg)
        {
            WetplateEffectsConfig snapshot = cfg?.Clone() ?? new WetplateEffectsConfig();
            snapshot.ClampInPlace();
            return snapshot;
        }
    }
}