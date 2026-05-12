using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Vintagestory.API.Config;

namespace Phototesting.ImageEffects
{
    // Persisted wetplate effects profile validation and disk I/O.
    // Keeps command parsing separate from profile storage concerns.
    internal static class WetplateEffectsProfileStore
    {
        private static readonly Regex _safeProfileName = new(
            "^[a-zA-Z0-9_\\-]+$",
            RegexOptions.Compiled);

        // Validates optional profile names used by effects save/load commands.
        internal static bool IsValidProfileName(string? name)
        {
            return string.IsNullOrWhiteSpace(name) || _safeProfileName.IsMatch(name);
        }

        // Builds the profile path under ModData/phototesting with a default effects-tuning file.
        internal static string GetProfilePath(string? name = null)
        {
            string file = string.IsNullOrWhiteSpace(name) ? "effects-tuning" : name;
            return Path.Combine(GamePaths.DataPath, "ModData", "phototesting", $"{file}.json");
        }

        // Writes a profile snapshot as formatted json.
        internal static void SaveProfile(string? name, WetplateEffectsConfig cfg)
        {
            string path = GetProfilePath(name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonConvert.SerializeObject(cfg, Formatting.Indented));
        }

        // Loads and deserializes a profile snapshot from disk.
        internal static WetplateEffectsConfig LoadProfile(string? name)
        {
            string path = GetProfilePath(name);
            string text = File.ReadAllText(path);
            WetplateEffectsConfig? loaded = JsonConvert.DeserializeObject<WetplateEffectsConfig>(text);

            if (loaded == null)
            {
                throw new InvalidOperationException("Effects load failed: file parsed as null.");
            }

            return loaded;
        }
    }
}
