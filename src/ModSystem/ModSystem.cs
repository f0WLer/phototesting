using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Phototesting.AdminTooling;
using Phototesting.CameraCapture;
using Phototesting.CameraCapture.Integration;
using Phototesting.ImageEffects;
using Phototesting.PhotoSync.Integration;
using Phototesting.PlateBox;
using Phototesting.PlateLifecycle;
using Phototesting.PlateLifecycle.GroundPlate;
using Phototesting.PlateLifecycle.Tray;
using Phototesting.Frame;

namespace Phototesting
{
    // Shared mod bootstrap, registration, and lifecycle cleanup for both sides.
    // Holds config, channels, and runtime references shared by the client/server partials.
    public partial class PhotoTestingModSystem : ModSystem
    {
        public static PhotoTestingModSystem? ClientInstance { get; internal set; }

        public const string ConfigFileName = "phototesting.json";
        public const string ServerPhotoIndexFileName = "phototesting-photoindex.json";
        public PhotoTestingConfig Config { get; internal set; } = new PhotoTestingConfig();
        public PhotoTestingClientConfig ClientConfig { get; internal set; } = new PhotoTestingClientConfig();
        public ProcessRegistry Processes { get; private set; } = new ProcessRegistry();

        // Applies a freshly loaded/normalized config tree, keeping ClientConfig in sync.
        internal void ApplyConfig(PhotoTestingConfig cfg)
        {
            Config = cfg;
            ClientConfig = cfg.Client;
        }

        public ICoreAPI? Api;
        public IClientNetworkChannel? ClientChannel;
        public IServerNetworkChannel? ServerChannel;
        internal ICoreClientAPI? ClientApi;

        private bool _effectProfilesSeeded;

        // Registers shared item/block classes and packet types used by both client and server startup paths.
        public override void Start(ICoreAPI api)
        {
            this.Api = api;

            Processes = new ProcessRegistry();
            Log.Notify(api.Logger, "Loaded {0} photography process(es): {1}",
                Processes.AllProcesses.Count,
                string.Join(", ", Processes.AllProcesses.Keys));

            api.RegisterItemClass("WetplateCamera", typeof(ItemWetplateCamera));
            api.RegisterItemClass("WetplateCameraTimer", typeof(ItemWetplateCameraTimer));
            api.RegisterItemClass("WetplateCameraAuto", typeof(ItemWetplateCameraAuto));
            api.RegisterItemClass("GlassPlate", typeof(ItemGlassPlate));
            api.RegisterItemClass("SensitizedPlate", typeof(ItemSensitizedPlate));
            api.RegisterItemClass("PhotoPlate", typeof(ItemPhotoPlate));

            api.RegisterBlockClass("GlassPlate", typeof(BlockGlassPlate));
            api.RegisterBlockClass("DevelopmentTray", typeof(BlockDevelopmentTray));
            api.RegisterBlockClass("PlateBox", typeof(BlockPlateBox));
            api.RegisterBlockClass("BlockFrame", typeof(BlockFrame));
            api.RegisterBlockClass("BlockMountedCamera", typeof(BlockMountedCamera));
            api.RegisterBlockEntityClass("BlockEntityDevelopmentTray", typeof(BlockEntityDevelopmentTray));
            api.RegisterBlockEntityClass("BlockEntityPlateBox", typeof(BlockEntityPlateBox));
            api.RegisterBlockEntityClass("BlockEntityPlateProcessState", typeof(BlockEntityPlateProcessState));
            api.RegisterBlockEntityClass("BlockEntityFrame", typeof(BlockEntityFrame));
            api.RegisterBlockEntityClass("BlockEntityMountedCamera", typeof(BlockEntityMountedCamera));

            // Register Network Channel
            var channel = CameraCaptureChannelRegistration.RegisterCameraCaptureMessageTypes(api.Network.RegisterChannel("phototesting"));

            CameraCaptureChannelRegistration.RegisterCameraCaptureConfigMessageTypes(PhotoSyncModSystemBridge.RegisterPhotoSyncMessageTypes(channel));
            AdminToolingChannelRegistration.RegisterAdminToolingMessageTypes(channel);
        }

        // Asset-backed process profile defaults are available from this lifecycle stage.
        // Seed once here so first world join has editable ModData profile files ready.
        public override void AssetsLoaded(ICoreAPI api)
        {
            if (api is not ICoreClientAPI capi) return;

            _effectProfilesSeeded = ClientEffectProfileSeeder.TryPrepare(
                capi,
                Processes,
                _effectProfilesSeeded,
                BestEffortLogger);
        }

        // Indicates whether best-effort failure details should be emitted for diagnostics.
        internal bool IsBestEffortDebugLoggingEnabled => ClientConfig?.ShowDebugLogs == true;

        // Shared logger gate used by partials when best-effort diagnostics should honor client debug verbosity.
        internal ILogger? BestEffortLogger => IsBestEffortDebugLoggingEnabled ? (Api ?? ClientApi)?.Logger : null;

        // Performs final teardown for renderer, listener, sync, and singleton references.
        public override void Dispose()
        {
            try
            {
                CameraCaptureBridge.DisposeClientCameraCaptureRenderers();
                CameraCaptureBridge.DisposeClientCameraCaptureTickListeners();
                DevelopmentTrayBridge.DisposeClientDevelopmentTrayTickListeners();

                if (Api is ICoreServerAPI sapi)
                {
                    PhotoSyncBridge.DisposeServerPhotoSyncAndMetadataRuntime(sapi);
                }
            }
            finally
            {
                CameraCaptureBridge.ClearClientCameraCaptureRuntimeReferences();
                PhotoSyncBridge.ClearPhotoSyncAndMetadataRuntimeReferences();
                ClientChannel = null;
                ServerChannel = null;

                if (ReferenceEquals(ClientInstance, this))
                {
                    ClientInstance = null;
                }

                base.Dispose();
            }
        }
    }
}
