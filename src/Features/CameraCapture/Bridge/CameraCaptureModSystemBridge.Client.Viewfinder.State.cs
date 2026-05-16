using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.Client.NoObf;

namespace Phototesting.CameraCapture
{
    // Client-side viewfinder: state machine, hold-still coordinator, capture gate, effects profile, and FOV zoom.
    internal sealed partial class CameraCaptureModSystemBridge
    {

    // Viewfinder mode state machine and zoom lifecycle.
    // Manages enter/exit behavior, zoom persistence, and FOV recovery via direct MainCamera.Fov mutation.

        private long _viewfinderTickListenerId;

        private ViewfinderHoldStillCoordinator? _holdStillCoordinator;
        private ViewfinderHoldStillCoordinator HoldStillCoordinator => _holdStillCoordinator ??= new ViewfinderHoldStillCoordinator(this);

        private bool _f4TipShownThisViewfinder;
        private bool _f4TipShownEver;

        private static readonly AssetLocation _viewfinderEnterSound = new AssetLocation("phototesting", "sounds/rustle");

        // Viewfinder effect: FOV zoom via direct MainCamera.Fov mutation.
        private readonly object _viewfinderLock = new object();
        private int _viewfinderDepth;
        private float? _viewfinderSavedFov;
        internal float _viewfinderTargetFov;

        private bool _zoomMechanismTipShownThisViewfinder;

        // Reports whether the nested client viewfinder mode depth is currently active.
        public bool IsViewfinderActive
        {
            get
            {
                lock (_viewfinderLock) return _viewfinderDepth > 0;
            }
        }

        // Indicates that capture has started but the hold-still packet cannot be sent yet.
        internal bool IsHoldStillPending => HoldStillCoordinator.IsPending;

        internal void UpdateHoldStill(float dt) => HoldStillCoordinator.Update(dt);
        internal void HoldStillNotifyPending() => HoldStillCoordinator.MaybeShowPendingMessage();
        internal void HoldStillMarkCaptureReady(string photoId) => HoldStillCoordinator.MarkCaptureReady(photoId);
        internal void HoldStillCancel() => HoldStillCoordinator.Cancel();
        internal void HoldStillStartTracking(EntityAgent player, string photoId) => HoldStillCoordinator.StartTracking(player, photoId);

        // Detects whether the camera item still has an in-flight timed exposure that should keep viewfinder mode alive.
        internal static bool IsTimedExposurePending(EntityAgent? byEntity)
        {
            ITreeAttribute? tree = byEntity?.Attributes?.GetTreeAttribute(ItemWetplateCamera.ExposureTimedAttrKey);
            if (tree == null) return false;

            return tree.GetInt(ItemWetplateCamera.ExposureTimedDurationMsKey, 0) > 0;
        }

        private float ViewfinderZoomMultiplierCfg => Config?.Viewfinder?.ZoomMultiplier ?? 0.65f;
        private float HoldStillDurationSecondsCfg => Config?.Viewfinder?.HoldStillDurationSeconds ?? 4f;
        private float HoldStillLookWeightCfg => Config?.Viewfinder?.HoldStillLookWeight ?? 0.35f;
        private float HoldStillLookContributionScaleCfg => Config?.Viewfinder?.HoldStillLookContributionScale ?? 2f;

        // Enters viewfinder mode, establishes the chosen zoom mechanism, and plays the local enter sound once for the outermost entry.
        public void BeginViewfinderMode()
        {
            if (ClientApi == null) return;

            lock (_viewfinderLock)
            {
                _viewfinderDepth++;
                if (_viewfinderDepth > 1) return;

                _viewfinderSavedFov = null;
                _zoomMechanismTipShownThisViewfinder = false;

                _f4TipShownThisViewfinder = false;
                MaybeShowF4GuiLessTip();

                IClientWorldAccessor? world = ClientApi.World;
                EntityAgent? playerEnt = world?.Player?.Entity;
                if (world != null && playerEnt != null)
                {
                    AudioUtils.FireAndForgetEntitySound(world, _viewfinderEnterSound, playerEnt, AudioUtils.NextRandomPitch(world));
                }

                ApplyZoomedFov();
            }
        }

        // Saves current MainCamera.Fov (radians) and applies the zoom multiplier directly.
        // This replaces the prior Harmony IL transpiler on ClientMain.Set3DProjection.
        private void ApplyZoomedFov()
        {
            if (ClientApi?.World is not ClientMain client || client.MainCamera == null) return;

            float current = client.MainCamera.Fov;
            if (_viewfinderSavedFov == null) _viewfinderSavedFov = current;

            float baseFov = _viewfinderSavedFov.Value;
            float zoomed = ClampZoomedFov(baseFov * ViewfinderZoomMultiplierCfg, baseFov);
            client.MainCamera.Fov = zoomed;
            _viewfinderTargetFov = zoomed;

            ClientApi.Render?.Reset3DProjection();
        }

        private static float ClampZoomedFov(float proposed, float oldValue)
        {
            // MainCamera.Fov is stored in radians (~0.3..2.5 rad covers typical FOV range).
            if (oldValue > 0f && oldValue < 10f)
            {
                return Math.Max(0.3f, Math.Min(2.5f, proposed));
            }
            return Math.Max(30f, Math.Min(110f, proposed));
        }

        // Exits viewfinder mode and restores the saved FOV for the outermost entry.
        public void EndViewfinderMode()
        {
            if (ClientApi == null) return;

            lock (_viewfinderLock)
            {
                if (_viewfinderDepth <= 0) return;
                _viewfinderDepth--;
                if (_viewfinderDepth > 0) return;

                if (_viewfinderSavedFov is float saved && ClientApi.World is ClientMain client && client.MainCamera != null)
                {
                    client.MainCamera.Fov = saved;
                    ClientApi.Render?.Reset3DProjection();
                }

                _viewfinderSavedFov = null;
                _viewfinderTargetFov = 0f;
            }
        }

        // Shows the one-time F4 tip that explains how to get a HUD-less viewfinder presentation.
        internal void MaybeShowF4GuiLessTip()
        {
            if (ClientApi == null) return;
            if (_f4TipShownThisViewfinder || _f4TipShownEver) return;
            if (IsGuiLessModeActive()) return;

            _f4TipShownThisViewfinder = true;
            _f4TipShownEver = true;
            ClientApi.ShowChatMessage("Wetplate: Tip - press F4 to toggle gui-less mode (hide HUD) while using the viewfinder.");
        }

        // True when the player has toggled HUD-less mode (F4). Reads the public engine flag.
        private bool IsGuiLessModeActive() => ClientApi?.HideGuis ?? false;

        // Reapplies the zoomed FOV if the engine reset MainCamera.Fov (e.g. user changed the FOV slider).
        internal void EnsureViewfinderZoomApplied()
        {
            if (ClientApi == null) return;

            lock (_viewfinderLock)
            {
                if (_viewfinderDepth <= 0) return;

                if (!_zoomMechanismTipShownThisViewfinder)
                {
                    _zoomMechanismTipShownThisViewfinder = true;
                    if (ClientConfig?.ShowDebugLogs == true)
                    {
                        ClientApi.ShowChatMessage("Wetplate: viewfinder zoom via MainCamera.Fov");
                    }
                }

                if (ClientApi.World is not ClientMain client || client.MainCamera == null) return;
                if (Math.Abs(client.MainCamera.Fov - _viewfinderTargetFov) > 0.001f)
                {
                    ApplyZoomedFov();
                }
            }
        }
    }
}
