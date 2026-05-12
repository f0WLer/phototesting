using Phototesting.PlateLifecycle.Tray.Config;
using Phototesting.AdminTooling;

namespace Phototesting.PlateLifecycle
{
    internal enum TrayActionKind
    {
        Developer,
        Fixer,
        Water
    }

    internal static class TrayDurationProvider
    {
        private const float DefaultDeveloperSeconds = 1.25f;
        private const float DefaultFixerSeconds = 1.25f;
        private const float DefaultWaterSeconds = 1.25f;

        // Resolves a tray action duration from the root config object.
        internal static float GetDurationSeconds(PhotoTestingConfig? config, TrayActionKind action)
        {
            return GetDurationSeconds(config?.DevelopmentTrayInteractions, action);
        }

        // Resolves and clamps the duration used for one tray action kind.
        internal static float GetDurationSeconds(DevelopmentTrayInteractionConfig? interactions, TrayActionKind action)
        {
            float seconds = action switch
            {
                TrayActionKind.Developer => interactions?.Developer?.DurationSeconds ?? DefaultDeveloperSeconds,
                TrayActionKind.Fixer => interactions?.Fixer?.DurationSeconds ?? DefaultFixerSeconds,
                _ => DefaultWaterSeconds
            };

            if (seconds < 0.05f) seconds = 0.05f;
            if (seconds > 30f) seconds = 30f;
            return seconds;
        }

        // Resolves and clamps how many chemical units each tray use should consume.
        internal static int GetChemicalUnitsPerUse(PhotoTestingConfig? config, int defaultUnits)
        {
            int amount = config?.PlateProcessing?.DevelopmentTrayChemicalUnitsPerUse ?? defaultUnits;
            if (amount < 1) amount = 1;
            return amount;
        }
    }
}

