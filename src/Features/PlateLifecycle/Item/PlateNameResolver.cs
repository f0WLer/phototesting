using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace Phototesting.PlateLifecycle
{
    public static class PlateNameResolver
    {
        public static string ResolveDisplayName(ICoreAPI? api, ItemStack stack, string fallback)
        {
            if (stack == null) return fallback;

            string? explicitNameKey = PlateStateService.GetNameLangCode(stack);
            if (!string.IsNullOrWhiteSpace(explicitNameKey))
            {
                return Lang.Get(explicitNameKey);
            }

            PlateStage stage = PlateStateService.GetStage(stack);
            string codePath = stack.Collectible?.Code?.Path ?? string.Empty;

            if (codePath.Equals("glassplate", System.StringComparison.OrdinalIgnoreCase))
            {
                if (stage == PlateStage.Clean) return Lang.Get("phototesting:plate-name-glass-clean");
                if (stage == PlateStage.Sensitizing) return Lang.Get("phototesting:plate-name-glass-sensitizing");
                return Lang.Get("phototesting:plate-name-glass");
            }

            PhotographyProcessDefinition process = ProcessRegistryLookup.ResolveProcessOrDefault(api, PlateStateService.GetProcessId(stack));

            if (codePath.Equals("sensitizedplate", System.StringComparison.OrdinalIgnoreCase))
            {
                if (stage == PlateStage.Exposed) return Lang.Get(process.ExposedPlateNameLangCode);
                return Lang.Get(process.SensitizedPlateNameLangCode);
            }

            if (codePath.Equals("photoplate", System.StringComparison.OrdinalIgnoreCase))
            {
                if (stage == PlateStage.Finished) return Lang.Get(process.FinishedPlateNameLangCode);
                return Lang.Get(process.DevelopedPlateNameLangCode);
            }

            return fallback;
        }
    }
}
