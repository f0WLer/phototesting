using System;
using System.Globalization;
using Phototesting.CameraCapture;
using Phototesting.PlateLifecycle.Rendering;

namespace Phototesting.AdminTooling
{
    // Miscellaneous .phototesting command handlers (version, preview).
    // Keeps utility command behavior isolated from effect-tuning command logic.
    internal sealed partial class AdminToolingModSystemBridge
    {
        // Prints loaded assembly version/build timestamp details for quick runtime verification.
        internal void HandleWetplateVersionCommand()
        {
            if (_owner.ClientApi == null) return;

            var asm = typeof(PhotoTestingModSystem).Assembly;
            string ver = asm.GetName().Version?.ToString() ?? "<nover>";
            string loc = asm.Location;
            string stamp = (!string.IsNullOrEmpty(loc) && System.IO.File.Exists(loc))
                ? BestEffort.Try(_owner.BestEffortLogger,
                    "read version command dll timestamp",
                    () => System.IO.File.GetLastWriteTime(loc).ToString("yyyy-MM-dd HH:mm:ss"),
                    "<unknown>")
                : "<unknown>";

            BestEffort.Try(_owner.BestEffortLogger,
                "report version command info",
                () => _owner.ClientApi.ShowChatMessage($"Wetplate: dll ver={ver} build={stamp}"));
            BestEffort.Try(_owner.BestEffortLogger,
                "report version command path",
                () => _owner.ClientApi.ShowChatMessage($"Wetplate: dll path={loc}"));
        }

        // Clears local photo and plate render caches so existing media is reloaded from disk.
        internal void HandleWetplateClearCacheCommand()
        {
            if (_owner.ClientApi == null) return;

            int clearedPlates = PhotoPlateRenderUtil.ClearClientRenderCacheAndBumpVersion();
            _owner.ClientApi.ShowChatMessage($"Wetplate: cleared {clearedPlates} plate renders (new photos will re-load from disk).");
        }

        // Handles debug preview toggles and sizing/quality subcommands for live viewfinder diagnostics.
        internal void HandleWetplatePreviewCommand(Vintagestory.API.Common.CmdArgs args)
        {
            if (_owner.ClientApi == null) return;

            var cfg = _owner.GetOrLoadClientConfig(_owner.ClientApi);
            cfg.Viewfinder ??= new ViewfinderConfig();

            string action = args.PopWord()?.ToLowerInvariant() ?? "show";
            bool changed = false;

            switch (action)
            {
                case "show":
                    break;

                case "peak":
                    string peakAction = args.PopWord()?.ToLowerInvariant() ?? "on";
                    switch (peakAction)
                    {
                        case "show":
                            break;

                        case "on":
                        case "enable":
                            cfg.Viewfinder.DebugPreviewPeak = true;
                            changed = true;
                            break;

                        case "off":
                        case "disable":
                            cfg.Viewfinder.DebugPreviewPeak = false;
                            changed = true;
                            break;

                        case "toggle":
                            cfg.Viewfinder.DebugPreviewPeak = !cfg.Viewfinder.DebugPreviewPeak;
                            changed = true;
                            break;

                        default:
                            _owner.ClientApi.ShowChatMessage("Wetplate: usage: .phototesting preview peak [show|on|off|toggle]");
                            return;
                    }
                    break;

                case "size":
                    {
                        string wStr = args.PopWord();
                        string hStr = args.PopWord();
                        if (!int.TryParse(wStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int w)
                            || !int.TryParse(hStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int h))
                        {
                            _owner.ClientApi.ShowChatMessage("Wetplate: usage: .phototesting preview size <width> <height>");
                            return;
                        }

                        cfg.Viewfinder.DebugPreviewWidth = w;
                        cfg.Viewfinder.DebugPreviewHeight = h;
                        changed = true;
                        break;
                    }

                case "refresh":
                    {
                        string msStr = args.PopWord();
                        if (!int.TryParse(msStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ms))
                        {
                            _owner.ClientApi.ShowChatMessage("Wetplate: usage: .phototesting preview refresh <milliseconds>");
                            return;
                        }

                        cfg.Viewfinder.DebugPreviewRefreshMs = ms;
                        changed = true;
                        break;
                    }

                case "anchor":
                    {
                        string anchor = args.PopWord();
                        if (string.IsNullOrWhiteSpace(anchor))
                        {
                            _owner.ClientApi.ShowChatMessage("Wetplate: usage: .phototesting preview anchor <topleft|topright|bottomleft|bottomright>");
                            return;
                        }

                        cfg.Viewfinder.DebugPreviewAnchor = anchor;
                        changed = true;
                        break;
                    }

                case "quality":
                    {
                        string dimStr = args.PopWord();
                        if (!int.TryParse(dimStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int dim))
                        {
                            _owner.ClientApi.ShowChatMessage($"Wetplate: usage: .phototesting preview quality <pixels> (current: {cfg.Viewfinder.DebugPreviewMaxDimension}, plate capture: {cfg.Viewfinder.PhotoCaptureMaxDimension})");
                            return;
                        }

                        cfg.Viewfinder.DebugPreviewMaxDimension = dim;
                        changed = true;
                        break;
                    }

                default:
                    _owner.ClientApi.ShowChatMessage("Wetplate: usage: .phototesting preview <show|size <w> <h>|refresh <ms>|anchor <pos>|peak [show|on|off|toggle]|quality <pixels>>");
                    return;
            }

            if (_owner.ClientApi != null && changed)
            {
                cfg.Viewfinder ??= new ViewfinderConfig();
                cfg.Viewfinder.ClampInPlace();
                _owner.SaveClientConfig(_owner.ClientApi);
            }

            _owner.ClientApi!.ShowChatMessage(
                $"Wetplate: preview peak={(cfg.Viewfinder.DebugPreviewPeak ? "on" : "off")}, "
                + $"{cfg.Viewfinder.DebugPreviewWidth}x{cfg.Viewfinder.DebugPreviewHeight}, "
                + $"refresh={cfg.Viewfinder.DebugPreviewRefreshMs}ms, anchor={cfg.Viewfinder.DebugPreviewAnchor}, "
                + $"quality={cfg.Viewfinder.DebugPreviewMaxDimension}px (plate={cfg.Viewfinder.PhotoCaptureMaxDimension}px)");
        }
    }
}

