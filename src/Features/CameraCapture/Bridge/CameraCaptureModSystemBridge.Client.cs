using HarmonyLib;
using Phototesting.AdminTooling;
using Phototesting.CameraCapture.Contracts;
using Phototesting.CameraCapture.Exposure;
using Phototesting.CameraCapture.Integration;
using Phototesting.CameraCapture.Rendering;
using Phototesting.ImageEffects;
using Phototesting.PhotoSync.Integration;
using Phototesting.PlateLifecycle;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Phototesting.CameraCapture
{
    // Client-side bridge: bootstrap, config sync, cleanup, and capture runtime coordination.
    internal sealed partial class CameraCaptureModSystemBridge
    {
        internal PhotoCaptureRenderer? _captureRenderer;
        private ViewfinderDebugPreviewRenderer? _debugPreviewRenderer;
        internal VirtualCaptureService? _virtualCaptureService;
        internal VirtualCameraPreviewRenderer? _virtualCameraPreviewRenderer;
        internal VirtualExposureRenderer? _virtualExposureRenderer;

        // Harmony instance that owns the self-portrait model-matrix postfix.
        // Applied once during client startup, removed on disposal.
        private Harmony? _selfPortraitHarmony;

        // Composes full client-side camera-capture startup behind one feature bootstrap entrypoint.
        internal void ConfigureClientCameraCaptureStartup(ICoreClientAPI api)
        {
            ConfigureClientCameraCaptureChannelHandlers();
            ConfigureClientCameraCaptureRenderers(api);
            ConfigureClientCameraCaptureInputAndProjection(api);

            // Patch EntityPlayerShapeRenderer to fix the self-portrait model-matrix offset.
            // EntityPlayerShapeRenderer lives in VSEssentials.dll which is a game content mod,
            // not directly referenceable at compile time — use AccessTools.TypeByName.
            // If the type is unavailable (extreme edge case), self-portrait silently degrades.
            _selfPortraitHarmony = new Harmony("phototesting.selfportrait");
            System.Type? playerShapeRendererType =
                AccessTools.TypeByName("Vintagestory.GameContent.EntityPlayerShapeRenderer");
            if (playerShapeRendererType != null)
            {
                _selfPortraitHarmony.Patch(
                    AccessTools.Method(playerShapeRendererType, "loadModelMatrixForPlayer"),
                    postfix: new HarmonyMethod(typeof(EntityPlayerSelfPortraitPatch), "Postfix"));
            }
        }

        // Registers client camera-capture packet handlers on the existing phototesting channel.
        private void ConfigureClientCameraCaptureChannelHandlers()
        {
            if (ClientChannel == null) return;

            _owner.PhotoSyncBridge.ConfigureClientPhotoSyncTransferChannelHandlers();
            CameraCaptureChannelRegistration.ConfigureClientHandlers(ClientChannel, OnPhotoCaptureConfigReceived);
        }

        // Wires client camera-capture renderers and requests server-authoritative capture config.
        private void ConfigureClientCameraCaptureRenderers(ICoreClientAPI api)
        {
            // Capture screenshots after the 3D scene is blitted to the default framebuffer,
            // but before GUI/HUD is rendered (EnumRenderStage.AfterBlit).
            _captureRenderer = new PhotoCaptureRenderer(api);
            _captureRenderer.SetCaptureMaxDimension(Config.Viewfinder.PhotoCaptureMaxDimension);
            api.Event.RegisterRenderer(_captureRenderer, EnumRenderStage.AfterBlit, "phototesting-photocapture");

            _virtualCameraPreviewRenderer = new VirtualCameraPreviewRenderer(api);
            api.Event.RegisterRenderer(_virtualCameraPreviewRenderer, EnumRenderStage.Before, "phototesting-virtualcamera-preview");

            _virtualExposureRenderer = new VirtualExposureRenderer(api);
            _virtualExposureRenderer.PreviewSink = _virtualCameraPreviewRenderer;
            api.Event.RegisterRenderer(_virtualExposureRenderer, EnumRenderStage.Before, "phototesting-virtualexposure");

            _debugPreviewRenderer = new ViewfinderDebugPreviewRenderer(api, _captureRenderer, () => IsViewfinderActive, _virtualCameraPreviewRenderer);
            api.Event.RegisterRenderer(_debugPreviewRenderer, EnumRenderStage.Ortho, "phototesting-viewfinder-preview");

            _virtualCaptureService = new VirtualCaptureService(api);

            // Ask server for authoritative capture sizing in multiplayer.
            // Some load orders/world joins invoke StartClientSide before the channel reports connected.
            // Defer send until connected so startup never aborts.
            TrySendPhotoCaptureConfigRequest(api);
        }

        // Wires camera-capture input polling and zoom projection patching.
        private void ConfigureClientCameraCaptureInputAndProjection(ICoreClientAPI api)
        {
            // Tick listener drives HoldStill timer + viewfinder lifecycle. RMB state is fed by
            // MouseDown/MouseUp events (below) so we never poll Input properties.
            _viewfinderTickListenerId = api.Event.RegisterGameTickListener(dt => CaptureClientRuntime.OnClientViewfinderTick(dt), 20, 0);

            CaptureClientRuntime.SubscribeMouseEvents(api);

            // Viewfinder zoom is applied directly via ClientMain.MainCamera.Fov in
            // BeginViewfinderMode/EndViewfinderMode (see Client.Viewfinder.State.cs).
        }
        private long? _clientCaptureConfigRetryTickListenerId;

        // Applies the server-authoritative capture size so multiplayer clients capture at the same resolution policy.
        private void OnPhotoCaptureConfigReceived(PhotoCaptureConfigPacket packet)
        {
            if (packet == null) return;

            Config = OperatorToolingConfigLifecycle.EnsureNormalized(Config);
            Config.Viewfinder.PhotoCaptureMaxDimension = packet.MaxDimension;
            Config.Viewfinder.ClampInPlace();

            _captureRenderer?.SetCaptureMaxDimension(Config.Viewfinder.PhotoCaptureMaxDimension);
        }

        // Sends the capture-config request immediately when connected or defers it until the client channel comes up.
        private void TrySendPhotoCaptureConfigRequest(ICoreClientAPI capi)
        {
            if (TrySendPhotoCaptureConfigRequestNow())
            {
                UnregisterClientCaptureConfigRetry(capi, "unregister immediate capture config retry listener");
                return;
            }

            EnsureClientCaptureConfigRetry(capi);
        }

        // Attempts one immediate config request send and reports whether startup sync completed.
        private bool TrySendPhotoCaptureConfigRequestNow()
        {
            if (ClientChannel == null || !ClientChannel.Connected) return false;

            try
            {
                ClientChannel.SendPacket(new PhotoCaptureConfigRequestPacket());
                return true;
            }
            catch
            {
                return false;
            }
        }

        // Registers one retry tick listener so config sync can wait for the client channel to connect.
        private void EnsureClientCaptureConfigRetry(ICoreClientAPI capi)
        {
            if (_clientCaptureConfigRetryTickListenerId.HasValue && _clientCaptureConfigRetryTickListenerId.Value > 0) return;

            _clientCaptureConfigRetryTickListenerId = capi.Event.RegisterGameTickListener(_ =>
            {
                if (!TrySendPhotoCaptureConfigRequestNow()) return;

                UnregisterClientCaptureConfigRetry(capi, "unregister delayed capture config retry listener");
            }, 200, 200);
        }

        // Removes the deferred config retry listener after success or during client shutdown.
        private void UnregisterClientCaptureConfigRetry(ICoreClientAPI capi, string operation)
        {
            if (!_clientCaptureConfigRetryTickListenerId.HasValue || _clientCaptureConfigRetryTickListenerId.Value <= 0) return;

            long id = _clientCaptureConfigRetryTickListenerId.Value;
            BestEffort.Try(BestEffortLogger, operation, () => capi.Event.UnregisterGameTickListener(id));
            _clientCaptureConfigRetryTickListenerId = null;
        }
        // Unregisters and disposes client renderers used by camera-capture and viewfinder preview flows.
        internal void DisposeClientCameraCaptureRenderers()
        {
            if (ClientApi == null) return;

            if (_captureRenderer != null)
            {
                BestEffort.Try(BestEffortLogger, "unregister capture renderer", () => ClientApi.Event.UnregisterRenderer(_captureRenderer, EnumRenderStage.AfterBlit));
                BestEffort.Try(BestEffortLogger, "dispose capture renderer", () => _captureRenderer.Dispose());
            }

            if (_debugPreviewRenderer != null)
            {
                BestEffort.Try(BestEffortLogger, "unregister debug preview renderer", () => ClientApi.Event.UnregisterRenderer(_debugPreviewRenderer, EnumRenderStage.Ortho));
                BestEffort.Try(BestEffortLogger, "dispose debug preview renderer", () => _debugPreviewRenderer.Dispose());
            }

            if (_virtualCameraPreviewRenderer != null)
            {
                BestEffort.Try(BestEffortLogger, "unregister virtual camera preview renderer", () => ClientApi.Event.UnregisterRenderer(_virtualCameraPreviewRenderer, EnumRenderStage.Before));
                BestEffort.Try(BestEffortLogger, "dispose virtual camera preview renderer", () => _virtualCameraPreviewRenderer.Dispose());
            }

            if (_virtualExposureRenderer != null)
            {
                BestEffort.Try(BestEffortLogger, "unregister virtual exposure renderer", () => ClientApi.Event.UnregisterRenderer(_virtualExposureRenderer, EnumRenderStage.Before));
                BestEffort.Try(BestEffortLogger, "dispose virtual exposure renderer", () => _virtualExposureRenderer.Dispose());
            }

            BestEffort.Try(BestEffortLogger, "dispose virtual capture service", () => _virtualCaptureService?.Dispose());

            // Remove the self-portrait Harmony patch so it doesn't linger across hot-reloads or mod unloads.
            BestEffort.Try(BestEffortLogger, "unpatch self-portrait harmony", () =>
            {
                _selfPortraitHarmony?.UnpatchAll("phototesting.selfportrait");
                _selfPortraitHarmony = null;
            });
        }

        // Unregisters camera-capture client tick listeners created during startup.
        internal void DisposeClientCameraCaptureTickListeners()
        {
            if (ClientApi == null) return;

            CaptureClientRuntime.UnsubscribeMouseEvents(ClientApi);

            if (_viewfinderTickListenerId > 0)
            {
                BestEffort.Try(BestEffortLogger, "unregister viewfinder tick listener", () => ClientApi.Event.UnregisterGameTickListener(_viewfinderTickListenerId));
                _viewfinderTickListenerId = 0;
            }

            UnregisterClientCaptureConfigRetry(ClientApi, "unregister capture config retry tick listener");
        }

        // Clears camera-capture runtime references after disposal to prevent stale instance reuse.
        internal void ClearClientCameraCaptureRuntimeReferences()
        {
            _captureRenderer = null;
            _debugPreviewRenderer = null;
            _virtualCameraPreviewRenderer = null;
            _virtualExposureRenderer = null;
            _cameraCaptureClientRuntime = null;
        }
    // Stateful client-side runtime for viewfinder input and shutter capture scheduling.
    // Keeps per-tick input state and capture lifecycle transitions outside the mod-system partial surface.

        private CameraCaptureClientRuntime? _cameraCaptureClientRuntime;
        private CameraCaptureClientRuntime CaptureClientRuntime => _cameraCaptureClientRuntime ??= new CameraCaptureClientRuntime(this);

        // Validates current camera state, schedules the screenshot, and queues the eventual authoritative PhotoTaken packet flow.
        internal bool RequestPhotoCaptureFromViewfinder(EntityAgent byEntity, bool silentIfBusy = false)
        {
            return CaptureClientRuntime.RequestPhotoCaptureFromViewfinder(byEntity, silentIfBusy);
        }

        // Resolves capture effects override for the currently loaded camera plate, if present.
        // Returns null to keep renderer on baseline profile when no loaded stack/process is available.
        internal WetplateEffectsConfig? ResolveCaptureEffectsOverrideForLoadedCameraPlate()
        {
            return CaptureEffectsProfileLookup.ResolveForLoadedCamera(this);
        }

    }
    internal sealed class CameraCaptureClientRuntime
    {
            private const float RmbReleaseGraceSeconds = 0.04f;

            private readonly CameraCaptureModSystemBridge _owner;

            private bool _suppressViewfinderUntilRmbReleased;
            private bool _captureInProgress;
            private float _rmbUpSeconds;
            private bool _lastRmbDown;
            private bool _rightMouseDown;
            private MouseEventDelegate? _mouseDownHandler;
            private MouseEventDelegate? _mouseUpHandler;
            private long _lastShutterGateChatMs;

            internal CameraCaptureClientRuntime(CameraCaptureModSystemBridge owner)
            {
                _owner = owner;
            }

            // Subscribes to MouseDown/MouseUp so RMB state is event-driven (no Input polling / no try-catch).
            internal void SubscribeMouseEvents(ICoreClientAPI api)
            {
                _mouseDownHandler = (MouseEvent e) => { if (e.Button == EnumMouseButton.Right) _rightMouseDown = true; };
                _mouseUpHandler = (MouseEvent e) => { if (e.Button == EnumMouseButton.Right) _rightMouseDown = false; };
                api.Event.MouseDown += _mouseDownHandler;
                api.Event.MouseUp += _mouseUpHandler;
            }

            internal void UnsubscribeMouseEvents(ICoreClientAPI api)
            {
                if (_mouseDownHandler != null)
                {
                    var d = _mouseDownHandler;
                    BestEffort.Try(_owner.BestEffortLogger, "unsubscribe viewfinder mousedown", () => api.Event.MouseDown -= d);
                    _mouseDownHandler = null;
                }
                if (_mouseUpHandler != null)
                {
                    var u = _mouseUpHandler;
                    BestEffort.Try(_owner.BestEffortLogger, "unsubscribe viewfinder mouseup", () => api.Event.MouseUp -= u);
                    _mouseUpHandler = null;
                }
                _rightMouseDown = false;
            }

            internal void OnClientViewfinderTick(float dt)
            {
                if (_owner.ClientApi == null) return;

                _owner.UpdateHoldStill(dt);

                ItemSlot? activeCameraSlot = CameraItemHelper.GetActiveCameraSlot(_owner.ClientApi);
                bool holdingCamera = activeCameraSlot != null;
                bool timedExposurePending = CameraCaptureModSystemBridge.IsTimedExposurePending(_owner.ClientApi.World.Player?.Entity);

                bool rightDown = GetRightMouseDown();

                bool rightPressed = rightDown && !_lastRmbDown;
                _lastRmbDown = rightDown;

                // Shift+RMB is reserved for loading a plate into the camera (no zoom/viewfinder).
                bool shiftDown = _owner.ClientApi.World.Player?.Entity?.Controls?.ShiftKey == true || _owner.ClientApi.World.Player?.Entity?.Controls?.Sneak == true;
                bool ctrlDown = _owner.ClientApi.World.Player?.Entity?.Controls?.CtrlKey == true;

                if (holdingCamera && shiftDown && !ctrlDown && rightDown && !_owner.IsViewfinderActive)
                {
                    // Prevent viewfinder from starting if the player releases shift while still holding RMB.
                    _suppressViewfinderUntilRmbReleased = true;

                    // Load/unload triggers only on edge press and only when networking is available.
                    if (!rightPressed || _owner.ClientChannel == null)
                    {
                        return;
                    }

                    ItemSlot? offhand = _owner.ClientApi.World.Player?.InventoryManager?.OffhandHotbarSlot;
                    ItemStack? offstack = offhand?.Itemstack;

                    bool cameraLoaded = CameraCaptureModSystemBridge.CameraHasLoadedPlate(activeCameraSlot?.Itemstack);

                    // Load: accept consolidated sensitized plates.
                    if (!cameraLoaded)
                    {
                        if (CameraPlateEligibility.CanLoadIntoCamera(offstack)) _owner.ClientChannel.SendPacket(new CameraLoadPlatePacket { Load = true });
                        return;
                    }

                    // Unload: only when offhand is empty.
                    if (offhand == null || !offhand.Empty) return;

                    if (_owner.IsHoldStillPending)
                    {
                        _owner.HoldStillNotifyPending();

                        return;
                    }

                    _owner.ClientChannel.SendPacket(new CameraLoadPlatePacket { Load = false });

                    return;
                }

                if (_owner.IsViewfinderActive) _owner.EnsureViewfinderZoomApplied();

                if (!holdingCamera)
                {
                    if (_captureInProgress) return;

                    _suppressViewfinderUntilRmbReleased = false;
                    _rmbUpSeconds = 0f;
                    if (_owner.IsViewfinderActive) _owner.EndViewfinderMode();
                    return;
                }

                if (!rightDown)
                {
                    if (timedExposurePending)
                    {
                        // Exposure already started: keep viewfinder active until timer completes.
                        _rmbUpSeconds = 0f;
                        if (!_owner.IsViewfinderActive) _owner.BeginViewfinderMode();
                        return;
                    }

                    _rmbUpSeconds += dt;
                    if (!_captureInProgress && _rmbUpSeconds > RmbReleaseGraceSeconds)
                    {
                        _suppressViewfinderUntilRmbReleased = false;
                        if (_owner.IsViewfinderActive) _owner.EndViewfinderMode();
                    }
                    return;
                }

                _rmbUpSeconds = 0f;

                // RMB is down and camera is held.
                if (_suppressViewfinderUntilRmbReleased) return;
                if (!_owner.IsViewfinderActive) _owner.BeginViewfinderMode();

                // Shutter capture is driven by ItemWetplateCamera held-interact callbacks while RMB
                // viewfinder is active so we can use the engine's standard timed interaction meter.
            }

            internal bool RequestPhotoCaptureFromViewfinder(EntityAgent byEntity, bool silentIfBusy)
            {
                bool isMounted = _owner._virtualCaptureService != null && IsHoldingTripod(_owner.ClientApi);

                if (!CameraCaptureModSystemBridge.CaptureGateService.TryValidateCaptureRequest(_owner, silentIfBusy, isMounted, out ItemStack? loadedPlateStack)) return false;

                var clientApi = _owner.ClientApi;
                if (clientApi == null) return false;

                // If the player wants an immersive, HUD-free viewfinder, rely on the game's built-in gui-less mode.
                _owner.MaybeShowF4GuiLessTip();

                // After taking a shot we want to exit viewfinder and not instantly re-enter until RMB is released.
                _suppressViewfinderUntilRmbReleased = true;

                WetplateEffectsConfig? effectsOverride = CameraCaptureModSystemBridge.CaptureEffectsProfileLookup.ResolveForLoadedPlate(_owner, loadedPlateStack);

                if (isMounted)
                {
                    return RequestVirtualCapture(clientApi, byEntity, effectsOverride, silentIfBusy);
                }

                PhotoCaptureRenderer? captureRenderer = _owner._captureRenderer;
                if (captureRenderer == null) return false;

                if (!captureRenderer.TryScheduleCapture(
                    out string fileName,
                    onSuccess: fn => OnCaptureSuccess(clientApi, byEntity, fn),
                    onError: ex => OnCaptureFailure(clientApi, ex),
                    effectsOverride: effectsOverride
                ))
                {
                    if (!silentIfBusy) clientApi.ShowChatMessage("Wetplate: capture already in progress...");
                    return false;
                }

                _captureInProgress = true;
                _owner.HoldStillStartTracking(byEntity, fileName);

                return true;
            }

            private bool RequestVirtualCapture(ICoreClientAPI clientApi, EntityAgent byEntity, WetplateEffectsConfig? effectsOverride, bool silentIfBusy)
            {
                VirtualCaptureService service = _owner._virtualCaptureService!;
                if (service.IsCapturing)
                {
                    if (!silentIfBusy) clientApi.ShowChatMessage("Wetplate: capture already in progress...");
                    return false;
                }

                Vintagestory.API.MathTools.Vec3d eyePos = byEntity.Pos.XYZ.AddCopy(0, byEntity.LocalEyePos.Y, 0);
                float yaw = byEntity.Pos.Yaw;
                float pitch = byEntity.Pos.Pitch;
                float fov = _owner._viewfinderTargetFov;
                int maxDimension = _owner.Config?.Viewfinder?.PhotoCaptureMaxDimension ?? ViewfinderConfig.DefaultPhotoCaptureMaxDimension;

                _captureInProgress = true;

                service.TryCaptureOneShot(
                    eyePos, yaw, pitch, fov, maxDimension, effectsOverride,
                    onSuccess: fn => OnCaptureSuccess(clientApi, byEntity, fn),
                    onError: ex => OnCaptureFailure(clientApi, ex));

                return true;
            }

            // Returns true when the player's offhand slot holds an item whose code path contains "tripod".
            // Placeholder until the tripod item exists; the check is intentionally loose.
            private static bool IsHoldingTripod(ICoreClientAPI? clientApi)
            {
                if (clientApi == null) return false;
                ItemSlot? offhand = clientApi.World.Player?.InventoryManager?.OffhandHotbarSlot;
                if (offhand == null || offhand.Empty) return false;
                return offhand.Itemstack?.Collectible?.Code?.Path?.Contains("tripod", StringComparison.OrdinalIgnoreCase) == true;
            }

            // Completes local post-capture flow once the renderer finishes writing the screenshot.
            private void OnCaptureSuccess(ICoreClientAPI clientApi, EntityAgent byEntity, string fileName)
            {
                _captureInProgress = false;

                // Send to server + play sound after capture completes.
                ClientPhotoSyncIntegration.NotifyPhotoCreated(clientApi, fileName);
                clientApi.World.PlaySoundAt(new AssetLocation("game:sounds/effect/woodclick"), byEntity, null, true, 32, 1f);

                _owner.HoldStillMarkCaptureReady(fileName);

                // Keep viewfinder open while timed exposure is active so exposure completion
                // logic in ItemWetplateCamera can run to the end.
                if (!CameraCaptureModSystemBridge.IsTimedExposurePending(byEntity)) _owner.EndViewfinderMode();
            }

            // Cancels hold-still state and exits viewfinder when the scheduled capture fails.
            private void OnCaptureFailure(ICoreClientAPI clientApi, Exception ex)
            {
                _captureInProgress = false;
                _owner.HoldStillCancel();
                clientApi.Logger.Error("Wetplate HUD-less capture failed: " + ex);
                clientApi.ShowChatMessage("Wetplate: capture failed (see log). Falling back may be needed.");

                // Still exit viewfinder (error means no screenshot was taken).
                _owner.EndViewfinderMode();
            }

            internal void ShowShutterGateMessageThrottled(string message)
            {
                if (_owner.ClientApi == null) return;

                long nowMs = Environment.TickCount64;
                if (nowMs - _lastShutterGateChatMs <= 1000) return;

                _lastShutterGateChatMs = nowMs;
                _owner.ClientApi.ShowChatMessage(message);
            }

            internal bool GetRightMouseDown()
            {
                return _rightMouseDown;
            }
        }
}
