using Phototesting.ImageEffects;

namespace Phototesting.AdminTooling
{
    // Client command router for .phototesting subcommands.
    // Dispatches to command partials without mixing command parsing into gameplay flows.
    internal sealed partial class AdminToolingModSystemBridge
    {
        private const string WetplatePreviewCommandArgs = "show|on|off|toggle|size <w> <h>|refresh <ms>|anchor <pos>|peak [show|on|off|toggle]|effects [show|on|off|toggle]|quality <px>|virtualcamera [stop]";
        private const string WetplateAvailableCommandsLine = "Phototesting: available commands: clearcache | preview (" + WetplatePreviewCommandArgs + ") | effects | effect <FieldName> <value> | effect save | effect load";
        private const string WetplateUnknownCommandTryLine = "Try: .phototesting clearcache | .phototesting preview (" + WetplatePreviewCommandArgs + ") | .phototesting effects | .phototesting effect <FieldName> <value>";

        // Routes .phototesting subcommands to their specialized handler partials.
        internal void OnWetplateClientCommand(int groupId, Vintagestory.API.Common.CmdArgs args)
        {
            if (_owner.ClientApi == null) return;

            string sub = args.PopWord();
            if (string.IsNullOrEmpty(sub))
            {
                _owner.ClientApi.ShowChatMessage(WetplateAvailableCommandsLine);
                return;
            }

            if (sub.Equals("ver", StringComparison.OrdinalIgnoreCase) || sub.Equals("version", StringComparison.OrdinalIgnoreCase))
            {
                HandleWetplateVersionCommand();
                return;
            }

            if (sub.Equals("clearcache", StringComparison.OrdinalIgnoreCase))
            {
                HandleWetplateClearCacheCommand();
                return;
            }

            if (sub.Equals("effects", StringComparison.OrdinalIgnoreCase) || sub.Equals("fx", StringComparison.OrdinalIgnoreCase))
            {
                ImageEffectsCommandModSystemBridge.HandleWetplateEffectsCommand(_owner, args);
                return;
            }

            if (sub.Equals("effect", StringComparison.OrdinalIgnoreCase))
            {
                ImageEffectsCommandModSystemBridge.HandleEffectFieldCommand(_owner, args);
                return;
            }

            if (sub.Equals("preview", StringComparison.OrdinalIgnoreCase))
            {
                HandleWetplatePreviewCommand(args);
                return;
            }

            _owner.ClientApi.ShowChatMessage($"Phototesting: unknown subcommand '{sub}'. {WetplateUnknownCommandTryLine}");
        }
    }
}
