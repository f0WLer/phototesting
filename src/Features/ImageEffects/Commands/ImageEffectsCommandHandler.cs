using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Phototesting.AdminTooling;

namespace Phototesting.ImageEffects
{
    // .phototesting effects/effect command behavior and persistence orchestration.
    internal static class ImageEffectsCommandHandler
    {
        // Handles .phototesting effects subcommands and persists any config mutations.
        internal static void HandleEffectsCommand(ICoreClientAPI capi, PhotoTestingConfig rootCfg, CmdArgs args, Action<PhotoTestingConfig> persistConfig)
        {
            rootCfg.Effects ??= new WetplateEffectsConfig();

            string param = args.PopWord();

            if (string.IsNullOrEmpty(param) || param.Equals("show", StringComparison.OrdinalIgnoreCase))
            {
                WetplateEffectsConfig cfg = rootCfg.Effects;
                capi.ShowChatMessage($"Wetplate effects: enabled={cfg.Enabled}");
                capi.ShowChatMessage($"  sepia={cfg.SepiaStrength:0.00} contrast={cfg.Contrast:0.00} brightness={cfg.Brightness:0.00}");
                capi.ShowChatMessage($"  curve: shoulder={cfg.HighlightShoulder:0.00} threshold={cfg.HighlightThreshold:0.00} shadowfloor={cfg.ShadowFloor:0.00} contraststart={cfg.ContrastStart:0.00}");
                capi.ShowChatMessage($"  vignette={cfg.Vignette:0.00} skyblowout={cfg.SkyBlowout:0.00} grain={cfg.Grain:0.00}");
                capi.ShowChatMessage($"  realism: imperfection={cfg.Imperfection:0.00} microblur={cfg.MicroBlur:0.00} skyuneven={cfg.SkyUnevenness:0.00} skytop={cfg.SkyTopFraction:0.00} edgewarmth={cfg.EdgeWarmth:0.00}");
                capi.ShowChatMessage($"  dust={cfg.DustCount} (opacity={cfg.DustOpacity:0.00})");
                capi.ShowChatMessage($"  scratches={cfg.ScratchCount} (opacity={cfg.ScratchOpacity:0.00})");
                capi.ShowChatMessage($"  dynamic={cfg.DynamicEnabled} dynamicscale={cfg.DynamicScale:0.00}");
                capi.ShowChatMessage("Usage: .phototesting effects <show|enable|disable|reset|preset|set> [param] [value]");
                return;
            }

            if (param.Equals("reset", StringComparison.OrdinalIgnoreCase))
            {
                rootCfg.Effects = new WetplateEffectsConfig();
                persistConfig(rootCfg);
                capi.ShowChatMessage("Wetplate: effects reset to defaults");
                return;
            }

            if (param.Equals("enable", StringComparison.OrdinalIgnoreCase))
            {
                WetplateEffectsConfig cfg = rootCfg.Effects;
                cfg.Enabled = true;
                persistConfig(rootCfg);
                capi.ShowChatMessage("Wetplate: effects enabled");
                return;
            }

            if (param.Equals("disable", StringComparison.OrdinalIgnoreCase))
            {
                WetplateEffectsConfig cfg = rootCfg.Effects;
                cfg.Enabled = false;
                persistConfig(rootCfg);
                capi.ShowChatMessage("Wetplate: effects disabled");
                return;
            }

            if (param.Equals("preset", StringComparison.OrdinalIgnoreCase))
            {
                string which = args.PopWord();
                if (string.IsNullOrEmpty(which))
                {
                    capi.ShowChatMessage("Wetplate: usage: .phototesting effects preset <indoor|outdoor>");
                    return;
                }

                bool isIndoor = which.Equals("indoor", StringComparison.OrdinalIgnoreCase);
                bool isOutdoor = which.Equals("outdoor", StringComparison.OrdinalIgnoreCase);

                if (!isIndoor && !isOutdoor)
                {
                    capi.ShowChatMessage("Wetplate: preset must be 'indoor' or 'outdoor'");
                    return;
                }

                WetplateEffectsConfig? preset = isIndoor ? rootCfg.EffectsPresetIndoor : rootCfg.EffectsPresetOutdoor;

                if (preset == null)
                {
                    // Seed from current active config
                    WetplateEffectsConfig current = rootCfg.Effects ?? new WetplateEffectsConfig();
                    preset = current.Clone();
                    if (isIndoor) rootCfg.EffectsPresetIndoor = preset.Clone();
                    else rootCfg.EffectsPresetOutdoor = preset.Clone();
                }

                preset.ClampInPlace();

                try
                {
                    // Activate preset by writing to the active config
                    rootCfg.Effects = preset.Clone();
                    persistConfig(rootCfg);
                    capi.ShowChatMessage($"Wetplate: preset '{which}' activated");
                }
                catch
                {
                    capi.ShowChatMessage("Wetplate: failed to activate preset");
                }
                return;
            }

            if (param.Equals("set", StringComparison.OrdinalIgnoreCase))
            {
                string prop = args.PopWord();
                string valStr = args.PopWord();

                if (string.IsNullOrEmpty(prop) || string.IsNullOrEmpty(valStr))
                {
                    capi.ShowChatMessage("Wetplate: usage: .phototesting effects set <property> <value>");
                    capi.ShowChatMessage("Properties: greyscale, pregrayred, pregraygreen, pregrayblue, sepia, contrast, brightness, shadowfloor, contraststart, highlightshoulder, highlightthreshold, vignette, vignetteradius, skyblowout, grain, imperfection, microblur, skyunevenness, skytopfraction, edgewarmth, dust, dustopacity, scratches, scratchopacity, dynamic, dynamicscale, halation, halationthreshold, halationradius, halationtint, lensaberration, lensaberrationstart, lensaberrationsigma, curveredtoe, curveredmid, curveredshoulder, curvegreentoe, curvegreenmid, curvegreenshoulder, curvebluetoe, curvebluemid, curveblueshoulder");
                    return;
                }

                WetplateEffectsConfig cfg = rootCfg.Effects;

                if (!ImageEffectsCommandPropertyMap.TryApply(prop, valStr, cfg, out string? setError))
                {
                    capi.ShowChatMessage(setError ?? "Wetplate: failed to set effect property");
                    return;
                }

                persistConfig(rootCfg);
                capi.ShowChatMessage($"Wetplate: set {prop} = {valStr}");
                capi.ShowChatMessage("Note: effects apply to newly taken photos. Use .phototesting clearcache to reload existing photos.");
                return;
            }

            capi.ShowChatMessage("Wetplate: usage: .phototesting effects <show|enable|disable|reset|preset|set>");
        }

        // Supports the legacy .phototesting effect alias while routing through current profile/property logic.
        internal static void HandleLegacyEffectCommand(ICoreClientAPI capi, PhotoTestingConfig rootCfg, CmdArgs args, Action<PhotoTestingConfig> persistConfig)
        {
            rootCfg.Effects ??= new WetplateEffectsConfig();

            string sub = args.PopWord();

            if (string.IsNullOrEmpty(sub))
            {
                capi.ShowChatMessage("Usage: .phototesting effect <property> <value>");
                capi.ShowChatMessage("       .phototesting effect save [name]  — save to <name>.json (default: effects-tuning.json)");
                capi.ShowChatMessage("       .phototesting effect load [name]  — load from <name>.json (default: effects-tuning.json)");
                capi.ShowChatMessage("This is a legacy alias for .phototesting effects set <property> <value>.");
                return;
            }

            if (sub.Equals("save", StringComparison.OrdinalIgnoreCase))
            {
                string? profileName = args.PopWord();
                if (!ImageEffectsProfileService.IsValidProfileName(profileName))
                {
                    capi.ShowChatMessage("Effects save failed: profile name may only contain letters, digits, hyphens, and underscores.");
                    return;
                }

                string path = ImageEffectsProfileService.GetNamedProfilePath(profileName);
                WetplateEffectsConfig cfg = rootCfg.Effects;
                cfg.ClampInPlace();
                try
                {
                    ImageEffectsProfileService.SaveNamedProfile(profileName, cfg);
                    capi.ShowChatMessage($"Effects profile saved to: {path}");
                }
                catch (Exception ex)
                {
                    capi.ShowChatMessage($"Effects save failed: {ex.Message}");
                }
                return;
            }

            if (sub.Equals("load", StringComparison.OrdinalIgnoreCase))
            {
                string? profileName = args.PopWord();
                if (!ImageEffectsProfileService.IsValidProfileName(profileName))
                {
                    capi.ShowChatMessage("Effects load failed: profile name may only contain letters, digits, hyphens, and underscores.");
                    return;
                }

                string path = ImageEffectsProfileService.GetNamedProfilePath(profileName);

                try
                {
                    WetplateEffectsConfig? loaded = ImageEffectsProfileService.TryLoadNamedProfile(profileName, capi);
                    if (loaded == null)
                    {
                        capi.ShowChatMessage($"No saved profile found at: {path}");
                        return;
                    }

                    rootCfg.Effects = loaded;
                    persistConfig(rootCfg);
                    capi.ShowChatMessage($"Effects profile loaded from: {path}");
                }
                catch (Exception ex)
                {
                    capi.ShowChatMessage($"Effects load failed: {ex.Message}");
                }
                return;
            }

            // sub is a legacy property alias; next arg is the value
            string propertyName = sub;
            string? valStr = args.PopWord();

            if (string.IsNullOrEmpty(valStr))
            {
                capi.ShowChatMessage($"Usage: .phototesting effect {propertyName} <value>");
                return;
            }

            WetplateEffectsConfig effectsCfg = rootCfg.Effects;
            if (!ImageEffectsCommandPropertyMap.TryApply(propertyName, valStr, effectsCfg, out string? error))
            {
                capi.ShowChatMessage(error ?? $"Wetplate: unknown property '{propertyName}'");
                capi.ShowChatMessage("Use .phototesting effects show to inspect current values and .phototesting effects set for the supported property list.");
                return;
            }

            persistConfig(rootCfg);
            capi.ShowChatMessage($"effect {propertyName} → {valStr}");
        }
    }
}