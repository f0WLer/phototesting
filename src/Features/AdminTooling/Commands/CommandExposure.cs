using System.Globalization;
using Phototesting.CameraCapture.Exposure;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace Phototesting.AdminTooling
{
    // .phototesting exposure command handler.
    // Controls the multi-frame virtual camera exposure session.
    // Commands: start [frames] | stop | pause | resume | reset | export | status
    internal sealed partial class AdminToolingModSystemBridge
    {
        private const int ExposureDefaultFrameCount = 8;

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
                    int frameCount = ExposureDefaultFrameCount;
                    string? framesStr = args.PopWord();
                    if (!string.IsNullOrEmpty(framesStr)
                        && int.TryParse(framesStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                        && parsed > 0)
                    {
                        frameCount = parsed;
                    }

                    var player = _owner.ClientApi.World.Player;
                    Vec3d eyePos = player.Entity.Pos.XYZ.AddCopy(0, player.Entity.LocalEyePos.Y, 0);
                    float yaw = player.Entity.Pos.Yaw;
                    float pitch = player.Entity.Pos.Pitch;
                    float fov = ((ClientMain)_owner.ClientApi.World).MainCamera.Fov;

                    renderer.Start(eyePos, yaw, pitch, fov, frameCount);
                    _owner.ClientApi.ShowChatMessage(
                        $"Wetplate: exposure started — {frameCount} frames at fov={fov:F2} rad.");
                    return;
                }

                case "stop":
                    renderer.Stop();
                    _owner.ClientApi.ShowChatMessage("Wetplate: exposure stopped and cleared.");
                    return;

                case "pause":
                    if (renderer.State == ExposureState.Capturing)
                    {
                        renderer.Pause();
                        _owner.ClientApi.ShowChatMessage(
                            $"Wetplate: exposure paused at {renderer.FramesAccumulated}/{renderer.TargetFrameCount} frames.");
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
                            $"Wetplate: exposure resumed ({renderer.FramesAccumulated}/{renderer.TargetFrameCount} frames so far).");
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
                    _owner.ClientApi.ShowChatMessage(
                        $"Wetplate: exposure — state={renderer.State}, frames={renderer.FramesAccumulated}/{renderer.TargetFrameCount}");
                    return;

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
                        "Wetplate: usage: .phototesting exposure <start [frames]|stop|pause|resume|reset|export|status|physics>");
                    return;
            }
        }
    }
}
