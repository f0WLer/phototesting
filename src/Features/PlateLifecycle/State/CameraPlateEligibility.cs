using Vintagestory.API.Common;

namespace Phototesting.PlateLifecycle
{
    /// <summary>
    /// Shared camera load/exposure plate eligibility rules used by both client and server paths.
    /// Keeps stage-first logic in one place for consolidated plate item families.
    /// </summary>
    public static class CameraPlateEligibility
    {
        private static readonly AssetLocation _glassPlateItemCode = new("phototesting", "glassplate");
        private static readonly AssetLocation _sensitizedPlateItemCode = new("phototesting", "sensitizedplate");
        private static readonly AssetLocation _photoPlateItemCode = new("phototesting", "photoplate");

        // Validates the compact loaded-plate code string used on camera item attributes.
        public static bool IsLoadedCodeSensitized(string? loadedCode)
        {
            if (string.IsNullOrWhiteSpace(loadedCode)) return false;
            return string.Equals(loadedCode, _sensitizedPlateItemCode.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        // Checks whether an item stack is a loadable sensitized plate for camera insertion.
        public static bool CanLoadIntoCamera(ItemStack? stack)
        {
            AssetLocation? code = stack?.Collectible?.Code;
            if (code == null) return false;
            if (code != _sensitizedPlateItemCode) return false;

            PlateStage stage = PlateStateService.GetStage(stack);
            return stage == PlateStage.Sensitized || stage == PlateStage.Exposed
                || stage == PlateStage.Exposing  || stage == PlateStage.ExposurePaused;
        }

        // Checks whether a plate can start or resume accumulation (Sensitized or ExposurePaused).
        public static bool IsPlateExposable(ItemStack? stack)
        {
            AssetLocation? code = stack?.Collectible?.Code;
            if (code == null) return false;
            if (code != _sensitizedPlateItemCode) return false;

            PlateStage stage = PlateStateService.GetStage(stack);
            return stage == PlateStage.Sensitized || stage == PlateStage.ExposurePaused;
        }

        // Checks whether a plate can be exposed now (must be sensitized stage, not just loadable).
        public static bool IsPlateSensitizedForExposure(ItemStack? stack)
        {
            AssetLocation? code = stack?.Collectible?.Code;
            if (code == null) return false;
            if (code != _sensitizedPlateItemCode) return false;

            return PlateStateService.GetStage(stack) == PlateStage.Sensitized;
        }
    }
}
