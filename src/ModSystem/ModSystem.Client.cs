using Vintagestory.API.Client;
using Phototesting.AdminTooling;

namespace Phototesting
{
    // Client startup wiring for channels, renderers, commands, and tick listeners.
    // Keeps client-only bootstrap and config persistence separate from server startup.
    public partial class PhotoTestingModSystem
    {
        // Client startup wires networking, renderers, hotkeys, config, and the viewfinder polling loop.
        public override void StartClientSide(ICoreClientAPI api)
        {
            AdminToolingBridge.ConfigureClientOperatorToolingStartup(api);
            PhotoSyncBridge.ConfigureClientPhotoSyncStartup();
            CameraCaptureBridge.ConfigureClientCameraCaptureStartup(api);
            DevelopmentTrayBridge.ConfigureClientDevelopmentTrayInputListeners(api);
        }

        // Lazily ensures the full client config tree is available before UI or render code reads from it.
        internal PhotoTestingConfig GetOrLoadClientConfig(ICoreClientAPI capi)
        {
            if (Config == null)
            {
                Config = OperatorToolingConfigLifecycle.LoadOrCreate(capi, ConfigFileName);
            }
            else
            {
                Config = OperatorToolingConfigLifecycle.EnsureNormalized(Config);
            }

            ClientConfig = Config.Client;
            return Config;
        }

        // Persists the current client config back to disk after clamping it into a safe range.
        internal void SaveClientConfig(ICoreClientAPI capi)
        {
            if (Config == null) return;

            OperatorToolingConfigLifecycle.TryStoreNormalized(capi, ConfigFileName, Config);
            Config = OperatorToolingConfigLifecycle.EnsureNormalized(Config);
            ClientConfig = Config.Client;

            // Keep runtime capture effects in sync with latest persisted config edits.
            CameraCaptureBridge.OnClientConfigSavedCameraCapture();
        }

    }
}

