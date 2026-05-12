using Phototesting.AdminTooling;

namespace Phototesting.ImageEffects
{
    // ModSystem bridge for effects command routes.
    // Delegates behavior to ImageEffects command services.
    internal static class ImageEffectsCommandModSystemBridge
    {
        // Handles the modern .phototesting effects command family by delegating to feature-owned handlers.
        internal static void HandleWetplateEffectsCommand(PhotoTestingModSystem owner, Vintagestory.API.Common.CmdArgs args)
        {
            if (owner.ClientApi == null) return;

            PhotoTestingConfig rootCfg = owner.GetOrLoadClientConfig(owner.ClientApi);
            rootCfg.Effects ??= new WetplateEffectsConfig();
            ImageEffectsCommandHandler.HandleEffectsCommand(
                owner.ClientApi,
                rootCfg,
                args,
                cfg => OperatorToolingCommandConfigPersistence.PersistEffectsConfig(owner, cfg));
        }

        // Handles legacy .phototesting effect aliases and profile save/load by delegating to feature handlers.
        internal static void HandleEffectFieldCommand(PhotoTestingModSystem owner, Vintagestory.API.Common.CmdArgs args)
        {
            if (owner.ClientApi == null) return;

            PhotoTestingConfig rootCfg = owner.GetOrLoadClientConfig(owner.ClientApi);
            rootCfg.Effects ??= new WetplateEffectsConfig();
            ImageEffectsCommandHandler.HandleLegacyEffectCommand(
                owner.ClientApi,
                rootCfg,
                args,
                cfg => OperatorToolingCommandConfigPersistence.PersistEffectsConfig(owner, cfg));
        }
    }
}
