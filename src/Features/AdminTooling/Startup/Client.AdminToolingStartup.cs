using Vintagestory.API.Client;

namespace Phototesting.AdminTooling
{
    // Client-side operator-tooling startup composition.
    // Keeps config bootstrap and startup diagnostics out of ModSystem callback bodies.
    internal sealed partial class AdminToolingModSystemBridge
    {
        private readonly PhotoTestingModSystem _owner;

        internal AdminToolingModSystemBridge(PhotoTestingModSystem owner)
        {
            _owner = owner;
        }

        // Composes full client-side operator-tooling startup so ModSystem root stays declarative.
        internal void ConfigureClientOperatorToolingStartup(ICoreClientAPI api)
        {
            ConfigureClientOperatorToolingCore(api);
            ConfigureClientOperatorToolingConfig(api);
            TryReportClientOperatorToolingStartupInfo();
            ConfigureClientOperatorToolingCommands(api);
        }

        private void ConfigureClientOperatorToolingCore(ICoreClientAPI api)
        {
            _owner.ClientApi = api;
            PhotoTestingModSystem.ClientInstance = _owner;
            _owner.ClientChannel = api.Network.GetChannel("phototesting");
        }

        private void ConfigureClientOperatorToolingConfig(ICoreClientAPI api)
        {
            _owner.ApplyConfig(OperatorToolingConfigLifecycle.LoadOrCreate(api, PhotoTestingModSystem.ConfigFileName));
        }

        private void TryReportClientOperatorToolingStartupInfo()
        {
            var asm = typeof(PhotoTestingModSystem).Assembly;
            string ver = asm.GetName().Version?.ToString() ?? "<nover>";
            string loc = asm.Location;
            string stamp = string.IsNullOrEmpty(loc)
                ? "<unknown>"
                : BestEffort.Try(_owner.BestEffortLogger,
                    "read client dll timestamp",
                    () => System.IO.File.GetLastWriteTime(loc).ToString("yyyy-MM-dd HH:mm:ss"),
                    "<unknown>");

            if (_owner.ClientConfig?.ShowDebugLogs == true)
            {
                BestEffort.Try(_owner.BestEffortLogger,
                    "report client startup version info",
                    () => _owner.ClientApi?.ShowChatMessage($"Phototesting: loaded mod dll (ver={ver}, build={stamp})"));
            }
        }
    }
}
