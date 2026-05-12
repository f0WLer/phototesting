using Phototesting.AdminTooling;
using Phototesting.CameraCapture;
using Phototesting.PhotoSync.Integration;
using Phototesting.PlateLifecycle.Tray;
namespace Phototesting
{
    // Lazy feature bridge instances used by the mod-system entrypoints.
    // Callsites use modSys.XxxBridge.Method() directly rather than forwarding each method here.
    public partial class PhotoTestingModSystem
    {
        private AdminToolingModSystemBridge? _adminToolingBridge;
        private CameraCaptureModSystemBridge? _cameraCaptureBridge;
        private PhotoSyncModSystemBridge? _photoSyncBridge;
        private DevelopmentTrayModSystemBridge? _developmentTrayBridge;

        internal AdminToolingModSystemBridge AdminToolingBridge => _adminToolingBridge ??= new AdminToolingModSystemBridge(this);
        internal CameraCaptureModSystemBridge CameraCaptureBridge => _cameraCaptureBridge ??= new CameraCaptureModSystemBridge(this);
        internal PhotoSyncModSystemBridge PhotoSyncBridge => _photoSyncBridge ??= new PhotoSyncModSystemBridge(this);
        internal DevelopmentTrayModSystemBridge DevelopmentTrayBridge => _developmentTrayBridge ??= new DevelopmentTrayModSystemBridge(this);
    }
}
