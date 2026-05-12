using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Phototesting.CameraCapture
{
    // Client-side viewfinder: state machine, hold-still coordinator, capture gate, effects profile, and zoom harmony patch.
    internal sealed partial class CameraCaptureModSystemBridge
    {

    // Viewfinder mode state machine and zoom mechanism lifecycle.
    // Manages enter/exit behavior, zoom persistence, and FOV/projection recovery.

        private long _viewfinderTickListenerId;

        private ViewfinderHoldStillCoordinator? _holdStillCoordinator;
        private ViewfinderHoldStillCoordinator HoldStillCoordinator => _holdStillCoordinator ??= new ViewfinderHoldStillCoordinator(this);

        private bool _f4TipShownThisViewfinder;
        private bool _f4TipShownEver;

        private static readonly string[] _viewfinderZoomSettingKeys = { "fieldOfView", "fpHandsFoV" };
        private static readonly AssetLocation _viewfinderEnterSound = new AssetLocation("phototesting", "sounds/rustle");

        // Viewfinder effect: FOV zoom
        private readonly object _viewfinderLock = new object();
        private int _viewfinderDepth;
        private readonly Dictionary<string, float> _viewfinderOldFloatSettings = new Dictionary<string, float>();
        private readonly Dictionary<string, float> _viewfinderTargetFloatSettings = new Dictionary<string, float>();

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

                _viewfinderOldFloatSettings.Clear();
                _viewfinderTargetFloatSettings.Clear();
                _zoomMechanismTipShownThisViewfinder = false;

                _f4TipShownThisViewfinder = false;
                MaybeShowF4GuiLessTip();

                IClientWorldAccessor? world = ClientApi.World;
                EntityAgent? playerEnt = world?.Player?.Entity;
                if (world != null && playerEnt != null)
                {
                    AudioUtils.FireAndForgetEntitySound(world, _viewfinderEnterSound, playerEnt, AudioUtils.NextRandomPitch(world));
                }

                // Preferred zoom: Spyglass-style patch of Set3DProjection(float,float).
                // This is the most reliable way to force a visual zoom on clients where settings
                // changes do not apply live.
                if (ViewfinderZoomHarmony.TryInstall(ClientApi, ClientConfig))
                {
                    ViewfinderZoomHarmony.Refresh(ClientApi);
                    return;
                }

                // Fallback: zoom via client settings keys.
                foreach (string key in _viewfinderZoomSettingKeys)
                {
                    if (!ClientApi.Settings.Float.Exists(key)) continue;
                    float old = ClientApi.Settings.Float.Get(key, 70f);
                    _viewfinderOldFloatSettings[key] = old;

                    float newFov = old * ViewfinderZoomMultiplierCfg;
                    newFov = Math.Max(30f, Math.Min(110f, newFov));
                    _viewfinderTargetFloatSettings[key] = newFov;
                    ClientApi.Settings.Float.Set(key, newFov, true);
                }
            }
        }

        // Exits viewfinder mode and restores whichever zoom path was active for the outermost entry.
        public void EndViewfinderMode()
        {
            if (ClientApi == null) return;

            lock (_viewfinderLock)
            {
                if (_viewfinderDepth <= 0) return;
                _viewfinderDepth--;
                if (_viewfinderDepth > 0) return;

                if (ViewfinderZoomHarmony.IsActive)
                {
                    ViewfinderZoomHarmony.Refresh(ClientApi);
                }

                foreach (var kvp in _viewfinderOldFloatSettings)
                {
                    if (ClientApi.Settings.Float.Exists(kvp.Key))
                    {
                        ClientApi.Settings.Float.Set(kvp.Key, kvp.Value, true);
                    }
                }

                _viewfinderOldFloatSettings.Clear();
                _viewfinderTargetFloatSettings.Clear();
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

        // Best-effort reflection lookup for the engine's gui-less mode flag across client versions.
        private bool IsGuiLessModeActive()
        {
            if (ClientApi == null) return false;

            try
            {
                var type = ClientApi.GetType();
                var property = type.GetProperty("HideGuis") ?? type.GetProperty("HideGUIs") ?? type.GetProperty("HideGui");
                if (property != null && property.PropertyType == typeof(bool))
                {
                    return (bool)(property.GetValue(ClientApi) ?? false);
                }
            }
            catch
            {
                // ignore
            }

            return false;
        }

        // Reapplies the current zoom mechanism when the engine resets FOV or projection during active viewfinder mode.
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
                        string? mechanism = ViewfinderZoomHarmony.MechanismDescription;
                        if (!string.IsNullOrEmpty(mechanism))
                        {
                            ClientApi.ShowChatMessage($"Wetplate: viewfinder zoom via {mechanism}");
                        }
                        else
                        {
                            ClientApi.ShowChatMessage("Wetplate: viewfinder zoom via Settings.Float (fallback)");
                        }
                    }
                }

                if (ViewfinderZoomHarmony.IsActive && _viewfinderTargetFloatSettings.Count == 0)
                {
                    if (!ViewfinderZoomHarmony.WasScaledRecently(250))
                    {
                        ViewfinderZoomHarmony.Refresh(ClientApi);
                    }
                    return;
                }

                foreach (var kvp in _viewfinderTargetFloatSettings)
                {
                    if (!ClientApi.Settings.Float.Exists(kvp.Key)) continue;
                    float current = ClientApi.Settings.Float.Get(kvp.Key, kvp.Value);
                    if (Math.Abs(current - kvp.Value) > 0.001f)
                    {
                        ClientApi.Settings.Float.Set(kvp.Key, kvp.Value, true);
                    }
                }
            }
        }
    }
}
