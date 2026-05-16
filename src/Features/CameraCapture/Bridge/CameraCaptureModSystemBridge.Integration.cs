using Phototesting.AdminTooling;
using Phototesting.PlateLifecycle;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Phototesting.CameraCapture
{
    // Integration bridge: cross-feature startup composition, channel registration, and owner reference.
    internal sealed partial class CameraCaptureModSystemBridge
    {
    // CameraCapture startup composition and packet registration wiring.
    // Keeps camera packet registration ownership in the CameraCapture feature root.

        private readonly PhotoTestingModSystem _owner;

        internal CameraCaptureModSystemBridge(PhotoTestingModSystem owner)
        {
            _owner = owner;
        }

        internal ICoreAPI? Api => _owner.Api;
        internal ICoreClientAPI? ClientApi => _owner.ClientApi;
        internal IClientNetworkChannel? ClientChannel => _owner.ClientChannel;
        internal IServerNetworkChannel? ServerChannel
        {
            get => _owner.ServerChannel;
            set => _owner.ServerChannel = value;
        }

        internal PhotoTestingConfig Config
        {
            get => _owner.Config;
            set
            {
                _owner.Config = value;
                _owner.ClientConfig = value.Client;
            }
        }

        internal PhotoTestingClientConfig ClientConfig => _owner.ClientConfig;
        internal ProcessRegistry Processes => _owner.Processes;
        internal ILogger? BestEffortLogger => _owner.BestEffortLogger;
        internal bool IsBestEffortDebugLoggingEnabled => _owner.IsBestEffortDebugLoggingEnabled;

        // Applies camera-capture runtime reactions needed after client config persistence.
        internal void OnClientConfigSavedCameraCapture()
        {
            _captureRenderer?.ReloadEffectsConfig();
        }

    }
}
