using Phototesting.CameraCapture.Exposure;
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

            api.Input.RegisterHotKey(
                "phototesting-exposuregui",
                "Phototesting: Open Exposure Physics GUI",
                GlKeys.Unknown,
                HotkeyType.GUIOrOtherControls);
            api.Input.SetHotKeyHandler("phototesting-exposuregui", _ =>
            {
                VirtualExposureRenderer? renderer = _owner.CameraCaptureBridge._virtualExposureRenderer;
                if (renderer == null) return false;
                _exposurePhysicsDialog ??= new GuiDialogExposurePhysics(api, renderer, _owner);
                if (_exposurePhysicsDialog.IsOpened()) _exposurePhysicsDialog.TryClose();
                else _exposurePhysicsDialog.TryOpen();
                return true;
            });
        }
    }
}
