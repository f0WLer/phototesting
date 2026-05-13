using Vintagestory.API.Common;
using Phototesting.AdminTooling;

namespace Phototesting
{
    // Plate attribute helpers and back-compat wrappers around the vanilla
    // EnumTransitionType.Dry pipeline. Keeps a stable public surface for callers
    // while delegating to PlateDryingTransition.
    public static class WetPlateAttrs
    {
        public const string PhotoId = "photoId";
        public const string HoldStillSeconds = "phototestingHoldStillSeconds";
        public const string HoldStillMovement = "phototestingHoldStillMovement";

        // Resolves configured wet lifetime with a safe fallback when config is unavailable.
        public static double ResolveWetDurationHours(ICoreAPI? api)
        {
            return PhotoTestingConfigAccess.ResolveConfig(api)?.PlateProcessing?.WetPlateDurationHours
                ?? 0.66;
        }

        // True once the plate has fully dried.
        public static bool IsDry(IWorldAccessor world, ItemStack stack)
            => PlateDryingTransition.IsDry(world, stack);

        // Appends localized wet/dry tooltip text derived from current transition state.
        public static void AppendWetnessInfo(IWorldAccessor world, ItemStack stack, System.Text.StringBuilder dsc)
            => PlateDryingTransition.AppendInfo(world, stack, dsc);
    }
}
