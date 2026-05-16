using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Phototesting.AdminTooling
{
    // Lightweight helpers to resolve PhotoTestingModSystem and config snapshots from APIs.
    // Avoids repeating ModLoader lookup boilerplate across files.
    internal static class PhotoTestingConfigAccess
    {
        // Resolves the shared mod system instance from a generic core API handle.
        internal static PhotoTestingModSystem? ResolveModSystem(ICoreAPI? api)
        {
            return api?.ModLoader?.GetModSystem<PhotoTestingModSystem>();
        }

        // Returns the shared runtime config snapshot for the provided API context.
        internal static PhotoTestingConfig? ResolveConfig(ICoreAPI? api)
        {
            return ResolveModSystem(api)?.Config;
        }

        // Returns the client-side config snapshot for rendering/input code paths.
        internal static PhotoTestingConfig? ResolveClientConfig(ICoreClientAPI? capi)
        {
            return ResolveModSystem(capi)?.Config;
        }
    }
}
