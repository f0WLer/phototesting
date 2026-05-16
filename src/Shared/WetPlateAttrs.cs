using Vintagestory.API.Common;
using Phototesting.AdminTooling;

namespace Phototesting
{
    // Plate attribute helpers.
    public static class WetPlateAttrs
    {
        // Resolves configured wet lifetime with a safe fallback when config is unavailable.
        public static double ResolveWetDurationHours(ICoreAPI? api)
        {
            return PhotoTestingConfigAccess.ResolveConfig(api)?.PlateProcessing?.WetPlateDurationHours
                ?? 0.66;
        }
    }
}
