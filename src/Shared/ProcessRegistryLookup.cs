using Vintagestory.API.Common;
using Phototesting.AdminTooling;
using Phototesting.PlateLifecycle;

namespace Phototesting
{
    // Resolves process definitions without exposing callers to mod-system lookup details.
    internal static class ProcessRegistryLookup
    {
        internal static ProcessRegistry? TryResolveRegistry(ICoreAPI? api)
        {
            return PhotoTestingConfigAccess.ResolveModSystem(api)?.Processes;
        }

        internal static PhotographyProcessDefinition ResolveProcessOrDefault(ICoreAPI? api, string? processId)
        {
            return TryResolveRegistry(api)?.ResolveOrDefault(processId) ?? ProcessRegistry.DefaultProcess;
        }
    }
}