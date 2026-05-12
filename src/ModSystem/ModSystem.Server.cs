using Vintagestory.API.Server;

namespace Phototesting
{
    // Server startup wiring for handlers, sync services, and authoritative config broadcast.
    // Registers packet entry points and periodic maintenance listeners used by camera/photo flows.
    public partial class PhotoTestingModSystem
    {
        // Initializes server config, packet handlers, sync services, and periodic maintenance listeners.
        public override void StartServerSide(ICoreServerAPI api)
        {
            AdminToolingBridge.ConfigureServerOperatorToolingStartup(api);
            CameraCaptureBridge.ConfigureServerCameraCaptureStartup(api);
        }
    }
}