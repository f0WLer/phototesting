using Vintagestory.API.Common;
using Phototesting.CameraCapture.Exposure;

namespace Phototesting.CameraCapture
{
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

        internal override ExposureStartOptions GetDefaultStartOptions() => ExposureStartOptions.TargetSamples();
    }
}
