using Vintagestory.API.Common;
using Phototesting.CameraCapture.Exposure;

namespace Phototesting.CameraCapture
{
    // Camera variant that automatically closes the shutter after a per-stack timer duration.
    // All viewfinder/tripod/plate-loading behaviour is identical to the base camera.
    // Timer duration defaults to DefaultTimerSeconds and is adjustable via Shift+scroll while holding the camera.
    public sealed class ItemWetplateCameraTimer : ItemWetplateCamera
    {
        internal const string TimerSecondsAttrKey  = "phototestingTimerSeconds";
        internal const float  DefaultTimerSeconds  = 20f;
        internal const float  MinTimerSeconds      = 5f;
        internal const float  MaxTimerSeconds      = 300f;
        internal const float  TimerStepSeconds     = 5f;

        private static readonly AssetLocation _baseCode             = new("phototesting", "wetplatecamera-timer");
        private static readonly AssetLocation _loadedSensitizedCode = new("phototesting", "wetplatecamera-timer-loaded-silvered");
        private static readonly AssetLocation _loadedExposedCode    = new("phototesting", "wetplatecamera-timer-loaded-exposed");

        internal override AssetLocation CameraBaseCode             => _baseCode;
        internal override AssetLocation CameraLoadedSensitizedCode => _loadedSensitizedCode;
        internal override AssetLocation CameraLoadedExposedCode    => _loadedExposedCode;

        // Reads the timer duration from the itemstack attributes, falling back to the default.
        internal static float ReadTimerSeconds(ItemStack? stack)
            => stack?.Attributes.GetFloat(TimerSecondsAttrKey, DefaultTimerSeconds) ?? DefaultTimerSeconds;

        // Writes a clamped timer duration to the itemstack attributes. Returns the clamped value.
        internal static float WriteTimerSeconds(ItemStack stack, float seconds)
        {
            float clamped = Math.Clamp(seconds, MinTimerSeconds, MaxTimerSeconds);
            stack.Attributes.SetFloat(TimerSecondsAttrKey, clamped);
            return clamped;
        }

        internal override ExposureStartOptions GetDefaultStartOptions(ItemStack? stack = null)
            => ExposureStartOptions.Timer(ReadTimerSeconds(stack));

        public override void GetHeldItemInfo(ItemSlot inSlot, System.Text.StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
            float timer = ReadTimerSeconds(inSlot?.Itemstack);
            dsc.AppendLine($"Timer: {timer:F0}s (Shift++ to adjust, {MinTimerSeconds:F0}–{MaxTimerSeconds:F0}s range)");
        }
    }

    // Camera variant that automatically closes the shutter once the plate's target sample count is reached.
    // All viewfinder/tripod/plate-loading behaviour is identical to the base camera.
    public sealed class ItemWetplateCameraAuto : ItemWetplateCamera
    {
        private static readonly AssetLocation _baseCode             = new("phototesting", "wetplatecamera-auto");
        private static readonly AssetLocation _loadedSensitizedCode = new("phototesting", "wetplatecamera-auto-loaded-silvered");
        private static readonly AssetLocation _loadedExposedCode    = new("phototesting", "wetplatecamera-auto-loaded-exposed");

        internal override AssetLocation CameraBaseCode             => _baseCode;
        internal override AssetLocation CameraLoadedSensitizedCode => _loadedSensitizedCode;
        internal override AssetLocation CameraLoadedExposedCode    => _loadedExposedCode;

        internal override ExposureStartOptions GetDefaultStartOptions(ItemStack? stack = null) => ExposureStartOptions.TargetSamples();
    }
}
