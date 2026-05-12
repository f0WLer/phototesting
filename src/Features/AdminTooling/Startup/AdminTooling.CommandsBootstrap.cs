using Vintagestory.API.Client;

namespace Phototesting.AdminTooling
{
    // Client operator-tooling command registration helper.
    // Keeps command registration details out of ModSystem startup callback bodies.
    internal sealed partial class AdminToolingModSystemBridge
    {
        internal void ConfigureClientOperatorToolingCommands(ICoreClientAPI api)
        {
#pragma warning disable CS0618 // Keep legacy command registration for compatibility
            api.RegisterCommand(
                "phototesting",
                "Phototesting mod commands",
                ".phototesting clearcache | .phototesting preview | .phototesting effects",
                OnWetplateClientCommand
            );
#pragma warning restore CS0618
        }
    }
}
