using Vintagestory.API.Server;

namespace Phototesting.AdminTooling
{
    // Server-side operator-tooling startup composition.
    // Keeps config bootstrap/logging details out of ModSystem callback bodies.
    internal sealed partial class AdminToolingModSystemBridge
    {
        internal void ConfigureServerOperatorToolingStartup(ICoreServerAPI api)
        {
            _owner.ApplyConfig(OperatorToolingConfigLifecycle.LoadOrCreate(api, PhotoTestingModSystem.ConfigFileName));

            BestEffort.Try(_owner.BestEffortLogger,
                "log server config load",
                () => api.Logger.Notification($"Phototesting: loaded config '{PhotoTestingModSystem.ConfigFileName}'"));
        }
    }
}
