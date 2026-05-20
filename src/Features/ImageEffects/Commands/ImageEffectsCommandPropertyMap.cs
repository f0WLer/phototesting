using System.Globalization;

namespace Phototesting.ImageEffects
{
    // Effects command property parsing and assignment bindings.
    internal static class ImageEffectsCommandPropertyMap
    {
        private static readonly Dictionary<string, ImageEffectsPropertyBinding> _bindings =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["imperfection"] = ImageEffectsPropertyBinding.ForFloat((cfg, value) => cfg.Imperfection = value),
                ["microblur"] = ImageEffectsPropertyBinding.ForFloat((cfg, value) => cfg.MicroBlur = value),
                ["skyunevenness"] = ImageEffectsPropertyBinding.ForFloat((cfg, value) => cfg.SkyUnevenness = value),
                ["skyuneven"] = ImageEffectsPropertyBinding.ForFloat((cfg, value) => cfg.SkyUnevenness = value),
                ["skytopfraction"] = ImageEffectsPropertyBinding.ForFloat((cfg, value) => cfg.SkyTopFraction = value),
                ["skytop"] = ImageEffectsPropertyBinding.ForFloat((cfg, value) => cfg.SkyTopFraction = value),
                ["edgewarmth"] = ImageEffectsPropertyBinding.ForFloat((cfg, value) => cfg.EdgeWarmth = value),
                ["sepia"] = ImageEffectsPropertyBinding.ForFloat((cfg, value) => cfg.SepiaStrength = value),
                ["contrast"] = ImageEffectsPropertyBinding.ForFloat((cfg, value) => cfg.Contrast = value),
                ["highlightshoulder"] = ImageEffectsPropertyBinding.ForFloat((cfg, value) => cfg.HighlightShoulder = value),
                ["shoulder"] = ImageEffectsPropertyBinding.ForFloat((cfg, value) => cfg.HighlightShoulder = value),
                ["highlightthreshold"] = ImageEffectsPropertyBinding.ForFloat((cfg, value) => cfg.HighlightThreshold = value),
                ["threshold"] = ImageEffectsPropertyBinding.ForFloat((cfg, value) => cfg.HighlightThreshold = value),
                ["brightness"] = ImageEffectsPropertyBinding.ForFloat((cfg, value) => cfg.Brightness = value),
                ["shadowfloor"] = ImageEffectsPropertyBinding.ForFloat((cfg, value) => cfg.ShadowFloor = value),
                ["contraststart"] = ImageEffectsPropertyBinding.ForFloat((cfg, value) => cfg.ContrastStart = value),
                ["vignette"] = ImageEffectsPropertyBinding.ForFloat((cfg, value) => cfg.Vignette = value),
                ["skyblowout"] = ImageEffectsPropertyBinding.ForFloat((cfg, value) => cfg.SkyBlowout = value),
                ["grain"] = ImageEffectsPropertyBinding.ForFloat((cfg, value) => cfg.Grain = value),
                ["dust"] = ImageEffectsPropertyBinding.ForInt("dust", (cfg, value) => cfg.DustCount = value),
                ["dustopacity"] = ImageEffectsPropertyBinding.ForFloat((cfg, value) => cfg.DustOpacity = value),
                ["scratches"] = ImageEffectsPropertyBinding.ForInt("scratches", (cfg, value) => cfg.ScratchCount = value),
                ["scratchopacity"] = ImageEffectsPropertyBinding.ForFloat((cfg, value) => cfg.ScratchOpacity = value),
                ["dynamic"] = ImageEffectsPropertyBinding.ForBool("dynamic", (cfg, value) => cfg.DynamicEnabled = value),
                ["dynamicenabled"] = ImageEffectsPropertyBinding.ForBool("dynamic", (cfg, value) => cfg.DynamicEnabled = value),
                ["dynamicscale"] = ImageEffectsPropertyBinding.ForFloat((cfg, value) => cfg.DynamicScale = value),
                ["vignetteradius"] = ImageEffectsPropertyBinding.ForFloat((cfg, value) => cfg.VignetteRadius = value),
                ["halation"] = ImageEffectsPropertyBinding.ForFloat((cfg, value) => cfg.Halation = value),
                ["halationthreshold"] = ImageEffectsPropertyBinding.ForFloat((cfg, value) => cfg.HalationThreshold = value),
                ["halationradius"] = ImageEffectsPropertyBinding.ForFloat((cfg, value) => cfg.HalationRadius = value),
                ["halationtint"] = ImageEffectsPropertyBinding.ForFloat((cfg, value) => cfg.HalationTint = value),
                ["lensaberration"] = ImageEffectsPropertyBinding.ForFloat((cfg, value) => cfg.LensAberration = value),
                ["lensaberrationstart"] = ImageEffectsPropertyBinding.ForFloat((cfg, value) => cfg.LensAberrationStart = value),
                ["lensaberrationsigma"] = ImageEffectsPropertyBinding.ForFloat((cfg, value) => cfg.LensAberrationSigma = value)
            };

        // Resolves a property alias and applies the parsed value through its typed binding.
        internal static bool TryApply(string property, string rawValue, WetplateEffectsConfig cfg, out string? error)
        {
            if (!_bindings.TryGetValue(property, out ImageEffectsPropertyBinding? binding))
            {
                error = $"Wetplate: unknown property '{property}'";
                return false;
            }

            return binding.TryApply(cfg, rawValue, out error);
        }

        // Parses an invariant-culture float used by numeric effect settings.
        internal static bool TryParseFloat(string rawValue, out float value, out string? error)
        {
            if (!float.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                error = "Wetplate: value must be a number (use . not ,)";
                return false;
            }

            error = null;
            return true;
        }

        // Parses an invariant-culture integer and returns a label-specific error message when invalid.
        internal static bool TryParseInt(string rawValue, string label, out int value, out string? error)
        {
            if (!int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                error = $"Wetplate: {label} must be an integer";
                return false;
            }

            error = null;
            return true;
        }

        // Parses a bool and returns a label-specific error message when invalid.
        internal static bool TryParseBool(string rawValue, string label, out bool value, out string? error)
        {
            if (!bool.TryParse(rawValue, out value))
            {
                error = $"Wetplate: {label} value must be true/false";
                return false;
            }

            error = null;
            return true;
        }
    }

    internal sealed class ImageEffectsPropertyBinding
    {
        private readonly Action<WetplateEffectsConfig, float>? _setFloat;
        private readonly Action<WetplateEffectsConfig, int>? _setInt;
        private readonly Action<WetplateEffectsConfig, bool>? _setBool;
        private readonly string? _label;

        // Creates a float-backed property binding.
        private ImageEffectsPropertyBinding(Action<WetplateEffectsConfig, float> setter)
        {
            _setFloat = setter;
        }

        // Creates an int-backed property binding with a user-facing label for parse errors.
        private ImageEffectsPropertyBinding(string intLabel, Action<WetplateEffectsConfig, int> setter)
        {
            _label = intLabel;
            _setInt = setter;
        }

        // Creates a bool-backed property binding with a user-facing label for parse errors.
        private ImageEffectsPropertyBinding(string boolLabel, Action<WetplateEffectsConfig, bool> setter)
        {
            _label = boolLabel;
            _setBool = setter;
        }

        // Builds a binding for float properties.
        internal static ImageEffectsPropertyBinding ForFloat(Action<WetplateEffectsConfig, float> setter)
        {
            return new ImageEffectsPropertyBinding(setter);
        }

        // Builds a binding for int properties.
        internal static ImageEffectsPropertyBinding ForInt(string label, Action<WetplateEffectsConfig, int> setter)
        {
            return new ImageEffectsPropertyBinding(label, setter);
        }

        // Builds a binding for bool properties.
        internal static ImageEffectsPropertyBinding ForBool(string label, Action<WetplateEffectsConfig, bool> setter)
        {
            return new ImageEffectsPropertyBinding(label, setter);
        }

        // Parses and applies one raw command value through the active typed setter.
        internal bool TryApply(WetplateEffectsConfig cfg, string rawValue, out string? error)
        {
            if (_setFloat != null)
            {
                if (!ImageEffectsCommandPropertyMap.TryParseFloat(rawValue, out float value, out error)) return false;
                _setFloat(cfg, value);
                return true;
            }

            if (_setInt != null)
            {
                if (!ImageEffectsCommandPropertyMap.TryParseInt(rawValue, _label!, out int value, out error)) return false;
                _setInt(cfg, value);
                return true;
            }

            if (_setBool != null)
            {
                if (!ImageEffectsCommandPropertyMap.TryParseBool(rawValue, _label!, out bool value, out error)) return false;
                _setBool(cfg, value);
                return true;
            }

            error = "Wetplate: unsupported effect property binding";
            return false;
        }
    }
}