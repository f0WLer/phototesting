using Vintagestory.API.Common;

namespace Phototesting.AdminTooling
{
    // Feature-owned config lifecycle policy used by client/server startup and runtime persistence paths.
    // Normalizes config trees consistently and keeps load/store error handling in one place.
    internal static class OperatorToolingConfigLifecycle
    {
        // Loads persisted config, falling back to defaults when missing/invalid.
        // Newly created defaults are persisted best-effort.
        internal static PhotoTestingConfig LoadOrCreate(ICoreAPICommon api, string fileName)
        {
            PhotoTestingConfig? cfg = null;
            try
            {
                cfg = api.LoadModConfig<PhotoTestingConfig>(fileName);
            }
            catch
            {
                cfg = null;
            }

            bool createdDefault = cfg == null;
            cfg = EnsureNormalized(cfg);

            if (createdDefault)
            {
                TryStore(api, fileName, cfg);
            }

            return cfg;
        }

        // Ensures a non-null, clamped config tree suitable for runtime reads.
        internal static PhotoTestingConfig EnsureNormalized(PhotoTestingConfig? cfg)
        {
            cfg ??= new PhotoTestingConfig();
            cfg.ClampInPlace();
            return cfg;
        }

        // Stores config best-effort without throwing into gameplay paths.
        internal static void TryStore(ICoreAPICommon api, string fileName, PhotoTestingConfig cfg)
        {
            try
            {
                api.StoreModConfig(cfg, fileName);
            }
            catch
            {
                // ignore
            }
        }

        // Normalizes config and stores it best-effort.
        internal static void TryStoreNormalized(ICoreAPICommon api, string fileName, PhotoTestingConfig? cfg)
        {
            if (cfg == null) return;
            TryStore(api, fileName, EnsureNormalized(cfg));
        }
    }
}
