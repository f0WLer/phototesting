using Phototesting.CameraCapture;
using Phototesting.CameraCapture.Exposure;
using Vintagestory.Client.NoObf;

namespace Phototesting.AdminTooling
{
    // .phototesting exposure command handler.
    // Controls the multi-frame virtual camera exposure session.
    // Commands: start [cap] | stop | discard | pause | resume | reset | export | ref [n] | status | physics
    internal sealed partial class AdminToolingModSystemBridge
    {

        internal void HandleWetplateExposureCommand(Vintagestory.API.Common.CmdArgs args)
        {
            if (_owner.ClientApi == null) return;

            VirtualExposureRenderer? renderer = _owner.CameraCaptureBridge._virtualExposureRenderer;
            if (renderer == null)
            {
                _owner.ClientApi.ShowChatMessage("Wetplate: exposure renderer is not available.");
                return;
            }

            string action = args.PopWord()?.ToLowerInvariant() ?? "status";

            switch (action)
            {
                case "start":
                {
                    // Optional process name — defaults to Iodide if omitted.
                    string? processArg = args.PopWord();
                    PlateProcessProfile process = PlateProcessProfile.Iodide;
                    if (!string.IsNullOrEmpty(processArg) && !PlateProcessProfile.TryParse(processArg, out process))
                    {
                        _owner.ClientApi.ShowChatMessage(
                            $"Wetplate: unknown process '{processArg}'. Use: chloride, iodide, bromide.");
                        return;
                    }

                    // Prefer the active virtual-camera preview so test exposures capture that exact view.
                    VirtualCameraPreviewRenderer? previewRenderer = _owner.CameraCaptureBridge._virtualCameraPreviewRenderer;
                    if (previewRenderer == null || !previewRenderer.TryGetActiveCameraState(out VirtualCameraState cameraState))
                    {
                        var player = _owner.ClientApi.World.Player;
                        var pos = player.Entity.SidedPos;
                        cameraState = new VirtualCameraState(
                            pos.XYZ.AddCopy(0, player.Entity.LocalEyePos.Y, 0),
                            pos.Yaw,
                            pos.Pitch,
                            ((ClientMain)_owner.ClientApi.World).MainCamera.Fov,
                            pos.Dimension);
                    }

                    renderer.Start(cameraState, process);
                    string portraitMsg = cameraState.SelfPortrait ? ", self-portrait" : "";
                    _owner.ClientApi.ShowChatMessage(
                        $"Wetplate: {process.Name} exposure started — {process.SampleCount} samples over {process.DurationSeconds}s{portraitMsg}. Use 'stop' to close shutter.");
                    return;
                }

                case "stop":
                    if (renderer.State != ExposureState.Capturing && renderer.State != ExposureState.Paused && renderer.State != ExposureState.Faulted)
                    {
                        _owner.ClientApi.ShowChatMessage($"Wetplate: cannot stop — state is {renderer.State}.");
                        return;
                    }
                    renderer.Stop();
                    _owner.ClientApi.ShowChatMessage(
                        $"Wetplate: shutter closed — {renderer.FramesAccumulated} frames accumulated. Use 'export' to save or 'discard' to clear.");
                    return;

                case "discard":
                    if (renderer.State == ExposureState.Idle)
                    {
                        _owner.ClientApi.ShowChatMessage("Wetplate: nothing to discard — already idle.");
                        return;
                    }
                    renderer.Discard();
                    _owner.ClientApi.ShowChatMessage("Wetplate: exposure discarded.");
                    return;

                case "pause":
                    if (renderer.State == ExposureState.Capturing)
                    {
                        renderer.Pause();
                        _owner.ClientApi.ShowChatMessage(
                            $"Wetplate: {renderer.ActiveProcess.Name} exposure paused at {renderer.FramesAccumulated}/{renderer.ActiveProcess.SampleCount} samples.");
                    }
                    else
                    {
                        _owner.ClientApi.ShowChatMessage($"Wetplate: cannot pause — state is {renderer.State}.");
                    }
                    return;

                case "resume":
                    if (renderer.State == ExposureState.Paused)
                    {
                        renderer.Resume();
                        _owner.ClientApi.ShowChatMessage(
                            $"Wetplate: {renderer.ActiveProcess.Name} exposure resumed — {renderer.FramesAccumulated}/{renderer.ActiveProcess.SampleCount} samples so far.");
                    }
                    else if (renderer.State == ExposureState.Faulted)
                    {
                        _owner.ClientApi.ShowChatMessage(
                            $"Wetplate: cannot resume — session faulted: {renderer.LastFaultMessage}. Use 'export' to save partial frames or 'discard' to clear.");
                    }
                    else
                    {
                        _owner.ClientApi.ShowChatMessage($"Wetplate: cannot resume — state is {renderer.State}.");
                    }
                    return;

                case "reset":
                    if (renderer.State == ExposureState.Idle)
                    {
                        _owner.ClientApi.ShowChatMessage("Wetplate: nothing to reset — exposure is idle.");
                        return;
                    }
                    renderer.Reset();
                    _owner.ClientApi.ShowChatMessage("Wetplate: exposure buffer cleared, capturing from same position.");
                    return;

                case "export":
                    if (renderer.FramesAccumulated == 0)
                    {
                        _owner.ClientApi.ShowChatMessage("Wetplate: nothing to export — no frames accumulated yet.");
                        return;
                    }
                    try
                    {
                        string fileName = renderer.Export();
                        _owner.ClientApi.ShowChatMessage(
                            $"Wetplate: exposure saved → {fileName} ({renderer.FramesAccumulated} frames).");
                    }
                    catch (Exception ex)
                    {
                        _owner.ClientApi.ShowChatMessage($"Wetplate: export failed — {ex.Message}");
                        _owner.ClientApi.Logger.Error($"Phototesting: exposure export error: {ex}");
                    }
                    return;

                case "status":
                {
                    PlateProcessProfile ap = renderer.ActiveProcess;
                    string faultSuffix = renderer.State == ExposureState.Faulted && renderer.LastFaultMessage != null
                        ? $", fault={renderer.LastFaultMessage}"
                        : string.Empty;
                    _owner.ClientApi.ShowChatMessage(
                        $"Wetplate: {ap.Name} — state={renderer.State}, " +
                        $"samples={renderer.FramesAccumulated} (target {ap.SampleCount}), " +
                        $"elapsed={renderer.ElapsedSeconds:F1}s / {ap.DurationSeconds}s, " +
                        $"interval={ap.SampleInterval:F3}s{faultSuffix}");
                    return;
                }

                case "process":
                {
                    string? nameArg = args.PopWord();
                    if (!string.IsNullOrEmpty(nameArg))
                    {
                        if (!PlateProcessProfile.TryParse(nameArg, out PlateProcessProfile queried))
                        {
                            _owner.ClientApi.ShowChatMessage($"Wetplate: unknown process '{nameArg}'. Use: chloride, iodide, bromide.");
                            return;
                        }
                        _owner.ClientApi.ShowChatMessage(
                            $"Wetplate: {queried.Name} — {queried.SampleCount} samples over {queried.DurationSeconds}s, " +
                            $"interval={queried.SampleInterval:F3}s, " +
                            $"R/G/B sensitivity={queried.RedSensitivity:F2}/{queried.GreenSensitivity:F2}/{queried.BlueSensitivity:F2}");
                    }
                    else
                    {
                        PlateProcessProfile ap = renderer.ActiveProcess;
                        _owner.ClientApi.ShowChatMessage(
                            $"Wetplate: active process is {ap.Name} — use 'start <process>' to change (chloride / iodide / bromide).");
                    }
                    return;
                }

                case "physics":
                case "phys":
                {
                    string? physFlag = args.PopWord()?.ToLowerInvariant();

                    // No flag — print current settings
                    if (string.IsNullOrEmpty(physFlag) || physFlag == "status")
                    {
                        _owner.ClientApi.ShowChatMessage(
                            $"Wetplate: physics — " +
                            $"linearize={(renderer.PhysicsLinearize ? "on" : "off")}, " +
                            $"spectral={(renderer.PhysicsSpectralWeights ? "on" : "off")}, " +
                            $"hdcurve={(renderer.PhysicsHDCurve ? "on" : "off")}");
                        return;
                    }

                    string? onOffStr = args.PopWord()?.ToLowerInvariant();
                    bool? onOff = onOffStr switch
                    {
                        "on"  or "true"  or "1" or "yes" or "enable"  => true,
                        "off" or "false" or "0" or "no"  or "disable" => false,
                        _ => null
                    };

                    if (onOff == null)
                    {
                        _owner.ClientApi.ShowChatMessage(
                            "Wetplate: usage: .phototesting exposure physics <linearize|spectral|hdcurve> <on|off>");
                        return;
                    }

                    if (!renderer.SetPhysics(physFlag, onOff.Value))
                    {
                        _owner.ClientApi.ShowChatMessage(
                            $"Wetplate: unknown physics flag '{physFlag}'. Valid: linearize, spectral, hdcurve");
                        return;
                    }

                    _owner.ClientApi.ShowChatMessage(
                        $"Wetplate: physics {physFlag} = {(onOff.Value ? "on" : "off")}");
                    return;
                }

                default:
                    _owner.ClientApi.ShowChatMessage(
                        "Wetplate: usage: .phototesting exposure <start [process]|stop|discard|pause|resume|reset|export|process [name]|status|physics>");
                    return;
            }
        }
    }
}
