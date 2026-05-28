using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Phototesting.CameraCapture.Exposure;
using Vintagestory.Client.NoObf;

namespace Phototesting.CameraCapture
{
    // Client-side viewfinder: state machine, capture gate, effects profile, and FOV zoom.
    internal sealed partial class CameraCaptureModSystemBridge
    {

    // Viewfinder mode state machine and zoom lifecycle.
    // Manages enter/exit behavior, zoom persistence, and FOV recovery via direct MainCamera.Fov mutation.

        private long _viewfinderTickListenerId;

        private bool _f4TipShownThisViewfinder;
        private bool _f4TipShownEver;

        private static readonly AssetLocation _viewfinderEnterSound = new AssetLocation("phototesting", "sounds/rustle");

        // Viewfinder effect: FOV zoom via direct MainCamera.Fov mutation.
        private readonly object _viewfinderLock = new object();
        private int _viewfinderDepth;
        private float? _viewfinderSavedFov;
        internal float _viewfinderTargetFov;

        // Per-session zoom value (radians). 0 = not yet set; initialised from ZoomMultiplier on first viewfinder entry.
        private float _viewfinderZoomFovRad;

        private const float ZoomFovMinRad = 5f  * MathF.PI / 180f;   // 5°
        private const float ZoomFovMaxRad = 90f * MathF.PI / 180f;   // 90°
        internal const float ZoomFovStepRad = 5f * MathF.PI / 180f;  // 5° per scroll notch / key press

        private bool _zoomMechanismTipShownThisViewfinder;

        // Reports whether the nested client viewfinder mode depth is currently active.
        public bool IsViewfinderActive
        {
            get
            {
                lock (_viewfinderLock) return _viewfinderDepth > 0;
            }
        }

        // Active accumulator for the current viewfinder exposure session.
        // Kept alive across RMB releases so a paused exposure can be resumed.
        internal IGameplayExposureAccumulator? ActiveAccumulator { get; set; }

        // Stable identifier for the active or most recently paused exposure session.
        // Used client-side so manual sealing can evict the matching registry entry deterministically.
        internal string ActiveExposureId { get; set; } = string.Empty;

        // True while accumulation frames are being gathered (Capturing state).
        internal bool IsExposureCapturing => ActiveAccumulator?.State == ExposureState.Capturing;

        private float ViewfinderZoomMultiplierCfg => Config?.Viewfinder?.ZoomMultiplier ?? 0.65f;
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
                ViewportExposureSuppressContext.ViewfinderActive = true;
            }
        }

        // Saves current MainCamera.Fov (radians) and applies the zoom, initialising from the
        // multiplier on the first viewfinder entry of the session.
        private void ApplyZoomedFov()
        {
            if (ClientApi?.World is not ClientMain client || client.MainCamera == null) return;

            float current = client.MainCamera.Fov;
            if (_viewfinderSavedFov == null) _viewfinderSavedFov = current;

            if (_viewfinderZoomFovRad == 0f)
            {
                float baseFov = _viewfinderSavedFov.Value;
                float initial = baseFov * ViewfinderZoomMultiplierCfg;
                _viewfinderZoomFovRad = Math.Max(ZoomFovMinRad, Math.Min(ZoomFovMaxRad, initial));
            }

            client.MainCamera.Fov = _viewfinderZoomFovRad;
            _viewfinderTargetFov = _viewfinderZoomFovRad;

            ClientApi.Render?.Reset3DProjection();
        }

        // Adjusts the live viewfinder FOV by deltaRad. No-op when viewfinder is not active.
        internal void AdjustViewfinderZoom(float deltaRad)
        {
            lock (_viewfinderLock)
            {
                if (_viewfinderDepth == 0) return;

                float next = Math.Max(ZoomFovMinRad, Math.Min(ZoomFovMaxRad, _viewfinderZoomFovRad + deltaRad));
                if (next == _viewfinderZoomFovRad) return;

                _viewfinderZoomFovRad = next;

                if (ClientApi?.World is ClientMain client && client.MainCamera != null)
                {
                    client.MainCamera.Fov = _viewfinderZoomFovRad;
                    _viewfinderTargetFov  = _viewfinderZoomFovRad;
                    ClientApi.Render?.Reset3DProjection();
                }
            }
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
                ViewportExposureSuppressContext.ViewfinderActive = false;
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
