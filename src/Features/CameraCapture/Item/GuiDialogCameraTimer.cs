using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Phototesting.CameraCapture.Contracts;

namespace Phototesting.CameraCapture
{
    // Small dialog for adjusting the timer camera's exposure duration.
    // Opened by Shift+Plus when holding an ItemWetplateCameraTimer.
    // Up/Down arrows step the value; Set confirms and syncs to the server.
    internal sealed class GuiDialogCameraTimer : GuiDialog
    {
        private const long ServerSendCooldownMs = 1500;

        private readonly IClientNetworkChannel _channel;
        private float _pendingSeconds;
        private long  _lastServerSendMs;

        public override string ToggleKeyCombinationCode => string.Empty;

        internal GuiDialogCameraTimer(ICoreClientAPI capi, IClientNetworkChannel channel) : base(capi)
            => _channel = channel;

        internal void OpenFor(ItemStack cameraStack)
        {
            _pendingSeconds = ItemWetplateCameraTimer.ReadTimerSeconds(cameraStack);
            if (IsOpened()) { RefreshValueLabel(); return; }
            TryOpen();
        }

        public override void OnGuiOpened() => ComposeDialog();

        private void ComposeDialog()
        {
            const double dialogW = 200.0;
            const double btnW    = 36.0;
            const double labelW  = dialogW - btnW * 2 - 20.0;
            const double rowH    = 32.0;
            const double btnH    = 28.0;
            double y = 28.0;

            var downBounds  = ElementBounds.Fixed(0,                      y,     btnW,   btnH);
            var valueBounds = ElementBounds.Fixed(btnW + 4,               y + 4, labelW, 22);
            var upBounds    = ElementBounds.Fixed(btnW + 4 + labelW + 4,  y,     btnW,   btnH);
            y += rowH + 6;
            var setBounds   = ElementBounds.Fixed(dialogW / 2.0 - 40.0,   y,     80.0,   25.0);

            var bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;

            SingleComposer = capi.Gui
                .CreateCompo("phototesting-cameratimer", ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle))
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar("Timer Camera", () => TryClose())
                .BeginChildElements(bgBounds)
                .AddSmallButton("▼", OnDown, downBounds)
                .AddDynamicText($"{_pendingSeconds:F0}s", CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center), valueBounds, "lbl-timer")
                .AddSmallButton("▲", OnUp, upBounds)
                .AddSmallButton("Set", OnSet, setBounds)
                .EndChildElements()
                .Compose();
        }

        private bool OnUp()   => Step( ItemWetplateCameraTimer.TimerStepSeconds);
        private bool OnDown() => Step(-ItemWetplateCameraTimer.TimerStepSeconds);

        private bool Step(float delta)
        {
            _pendingSeconds = Math.Clamp(
                _pendingSeconds + delta,
                ItemWetplateCameraTimer.MinTimerSeconds,
                ItemWetplateCameraTimer.MaxTimerSeconds);
            RefreshValueLabel();
            return true;
        }

        private bool OnSet()
        {
            ItemStack? camStack = CameraItemHelper.GetActiveCameraStack(capi);
            if (camStack?.Item is ItemWetplateCameraTimer)
                ItemWetplateCameraTimer.WriteTimerSeconds(camStack, _pendingSeconds);

            long now = capi.ElapsedMilliseconds;
            if (now - _lastServerSendMs >= ServerSendCooldownMs)
            {
                _channel.SendPacket(new CameraTimerPacket { Seconds = _pendingSeconds });
                _lastServerSendMs = now;
            }

            TryClose();
            return true;
        }

        private void RefreshValueLabel()
            => SingleComposer?.GetDynamicText("lbl-timer")?.SetNewText($"{_pendingSeconds:F0}s");
    }
}

