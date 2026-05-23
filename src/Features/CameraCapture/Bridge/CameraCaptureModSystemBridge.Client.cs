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
                    prefix: new HarmonyMethod(typeof(EntityPlayerSelfPortraitPatch), "Prefix"),
                    postfix: new HarmonyMethod(typeof(EntityPlayerSelfPortraitPatch), "Postfix"));

                // Suppress the entire render (body + first-person hands) during viewport exposure.
                _selfPortraitHarmony.Patch(
                    AccessTools.Method(playerShapeRendererType, "DoRender3DOpaque"),
                    prefix: new HarmonyMethod(typeof(EntityPlayerSelfPortraitPatch), "SuppressPrefix"));
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

            BestEffort.Try(BestEffortLogger, "dispose viewfinder exposure registry", () =>
            {
                ViewfinderExposureRegistry.Clear();
                ActiveAccumulator = null;
                ActiveExposureId = string.Empty;
            });

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
            ActiveAccumulator = null;
            ActiveExposureId = string.Empty;
            _captureRenderer = null;
            _debugPreviewRenderer = null;
            _virtualCameraPreviewRenderer = null;
            _virtualExposureRenderer = null;
            _cameraCaptureClientRuntime = null;
        }

        // Routes the accumulation preview source to the debug preview renderer when present.
        // Accepts the interface; the renderer is only updated when the concrete type is ViewportExposureAccumulator.
        internal void SetAccumulationPreviewSource(IGameplayExposureAccumulator? source)
        {
            if (_debugPreviewRenderer != null)
                _debugPreviewRenderer.AccumulationSource = source as ViewportExposureAccumulator;
        }
    // Stateful client-side runtime for viewfinder input and shutter capture scheduling.
    // Keeps per-tick input state and capture lifecycle transitions outside the mod-system partial surface.

        private CameraCaptureClientRuntime? _cameraCaptureClientRuntime;
        private CameraCaptureClientRuntime CaptureClientRuntime => _cameraCaptureClientRuntime ??= new CameraCaptureClientRuntime(this);

        // Starts a manual accumulation exposure for a mounted (tripod-attached) camera.
        // Delegates to the viewfinder accumulation path until dedicated virtual accumulation is implemented.
        internal bool RequestMountedPhotoCapture(EntityAgent byEntity, bool silentIfBusy = false)
        {
            return CaptureClientRuntime.TryToggleViewfinderExposure(byEntity, silentIfBusy, ExposureStartOptions.Manual());
        }

        // Toggles viewport accumulation: starts/resumes when idle, pauses when capturing.
        // The stop policy is chosen by the camera item at exposure start.
        internal bool TryToggleViewfinderExposure(EntityAgent byEntity, bool silentIfBusy = false, ExposureStartOptions startOptions = default)
        {
            return CaptureClientRuntime.TryToggleViewfinderExposure(byEntity, silentIfBusy, startOptions);
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
            private bool _lastLmbDown;
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

                ItemSlot? activeCameraSlot = CameraItemHelper.GetActiveCameraSlot(_owner.ClientApi);
                bool holdingCamera = activeCameraSlot != null;

                // Track LMB edge unconditionally so the transition from RMB-held to free-running
                // exposure does not generate a spurious LMB press on the first tick.
                bool leftDown = _owner.ClientApi.World.Player?.Entity?.Controls?.LeftMouseDown == true;
                bool leftPressed = leftDown && !_lastLmbDown;
                _lastLmbDown = leftDown;

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
                    // While a capture is actively accumulating, keep the viewfinder alive even
                    // without RMB held, and listen for LMB to pause.
                    if (_owner.IsExposureCapturing)
                    {
                        _rmbUpSeconds = 0f;
                        if (!_owner.IsViewfinderActive) _owner.BeginViewfinderMode();
                        if (leftPressed)
                        {
                            var playerEntity = _owner.ClientApi.World.Player?.Entity;
                            if (playerEntity != null)
                                TryToggleViewfinderExposure(playerEntity, silentIfBusy: true);
                        }
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

            // Toggle: starts/resumes accumulation when not capturing; pauses (and optionally seals) when capturing.
            // startOptions only applies when starting a fresh exposure; resume and pause paths ignore it.
            internal bool TryToggleViewfinderExposure(EntityAgent byEntity, bool silentIfBusy, ExposureStartOptions startOptions = default)
            {
                var acc = _owner.ActiveAccumulator;

                if (acc?.IsCapturing == true)
                {
                    acc.Pause();

                    if (acc.FramesAccumulated >= acc.TargetFrames)
                        ExportAndSealExposure(byEntity);
                    else
                        SendExposureStatePacket(isExposing: false, acc.FramesAccumulated, _owner.ActiveExposureId, acc.TargetFrames);

                    // Exit viewfinder immediately when pausing if RMB is not held.
                    if (!GetRightMouseDown() && _owner.IsViewfinderActive)
                        _owner.EndViewfinderMode();

                    return true;
                }

                // Start or resume
                if (!CameraCaptureModSystemBridge.CaptureGateService.TryValidateCaptureRequest(_owner, silentIfBusy, isMounted: false, out ItemStack? loadedPlateStack)) return false;

                var clientApi = _owner.ClientApi;
                if (clientApi == null) return false;

                // Get or create a stable ID for this exposure session.
                string exposureId = loadedPlateStack?.Attributes?.GetString(PlateStateAttributes.ExposureId) ?? string.Empty;

                // Try to resume from an existing registry entry (same session, e.g. RMB released and re-pressed).
                if (!string.IsNullOrEmpty(exposureId) && ViewfinderExposureRegistry.TryGet(exposureId, out var existingAcc) && existingAcc != null
                    && existingAcc.State == ExposureState.Paused)
                {
                    _owner.ActiveAccumulator = existingAcc;
                    _owner.ActiveExposureId = exposureId;
                    existingAcc.Resume();
                    _owner.SetAccumulationPreviewSource(existingAcc);
                    SendExposureStatePacket(isExposing: true, existingAcc.FramesAccumulated, exposureId, existingAcc.TargetFrames);
                    return true;
                }

                // Fresh exposure: generate a new session ID and allocate a new accumulator.
                exposureId = Guid.NewGuid().ToString("N");

                string processId = PlateStateService.GetProcessId(loadedPlateStack);
                if (!PlateProcessProfile.TryParse(processId, out PlateProcessProfile profile))
                    profile = PlateProcessProfile.Iodide;

                var newAcc = new ViewportExposureAccumulator(clientApi);
                newAcc.OnAutoHalt = () => OnAccumulatorAutoHalt(byEntity, newAcc, exposureId);
                newAcc.Start(profile, startOptions);

                ViewfinderExposureRegistry.Register(exposureId, newAcc);
                _owner.ActiveAccumulator = newAcc;
                _owner.ActiveExposureId = exposureId;
                _owner.SetAccumulationPreviewSource(newAcc);

                _owner.MaybeShowF4GuiLessTip();
                SendExposureStatePacket(isExposing: true, 0, exposureId, newAcc.TargetFrames);

                return true;
            }

            // Called by the accumulator's auto-halt callback once target frames are reached.
            private void OnAccumulatorAutoHalt(EntityAgent byEntity, ViewportExposureAccumulator acc, string exposureId)
            {
                // Auto-halt exits the viewfinder and seals the exposure.
                _suppressViewfinderUntilRmbReleased = true;
                ExportAndSealExposure(byEntity, exposureId);
                if (_owner.IsViewfinderActive) _owner.EndViewfinderMode();
            }

            // Develops and exports the current accumulation buffer, sends PhotoTakenPacket, and cleans up.
            private void ExportAndSealExposure(EntityAgent? byEntity, string? knownExposureId = null)
            {
                var acc = _owner.ActiveAccumulator;
                if (acc == null || acc.FramesAccumulated == 0)
                {
                    _owner.ActiveAccumulator = null;
                    return;
                }

                try
                {
                    var clientApi = _owner.ClientApi;
                    if (clientApi == null) return;

                    // Resolve per-plate effects override.
                    ItemStack? camStack = CameraItemHelper.GetActiveCameraStack(clientApi);
                    CameraItemHelper.TryGetLoadedPlateStack(camStack, clientApi.World, out ItemStack? loadedPlate);
                    WetplateEffectsConfig? effectsOverride = CameraCaptureModSystemBridge.CaptureEffectsProfileLookup.ResolveForLoadedPlate(_owner, loadedPlate);

                    acc.Stop();
                    string fileName = acc.Export(effectsOverride);

                    // Notify server to transition plate to Exposed.
                    _owner.ClientChannel?.SendPacket(new PhotoTakenPacket { PhotoId = fileName });
                    ClientPhotoSyncIntegration.NotifyPhotoCreated(clientApi, fileName);

                    // Authorize the expected photo upload.
                    if (byEntity != null)
                        clientApi.World.PlaySoundAt(new AssetLocation("game:sounds/effect/woodclick"), byEntity, null, true, 32, 1f);

                    // Evict the registry entry now that this session is sealed.
                    string exposureId = knownExposureId
                        ?? _owner.ActiveExposureId
                        ?? loadedPlate?.Attributes?.GetString(PlateStateAttributes.ExposureId)
                        ?? string.Empty;
                    if (!string.IsNullOrEmpty(exposureId))
                        ViewfinderExposureRegistry.Remove(exposureId);
                }
                catch (Exception ex)
                {
                    _owner.ClientApi?.Logger.Error("Phototesting: accumulation export failed — " + ex);
                }
                finally
                {
                    _owner.ActiveAccumulator = null;
                    _owner.ActiveExposureId = string.Empty;
                    _owner.SetAccumulationPreviewSource(null);
                }
            }

            // Sends an ExposureStatePacket to the server to keep plate attributes in sync.
            private void SendExposureStatePacket(bool isExposing, int exposedFrames, string exposureId, int targetFrames)
            {
                _owner.ClientChannel?.SendPacket(new ExposureStatePacket
                {
                    IsExposing = isExposing,
                    ExposureId = exposureId,
                    ExposedFrames = exposedFrames,
                    TargetFrames = targetFrames
                });
            }


            // Completes local post-capture flow once the renderer finishes writing the screenshot.
            private void OnCaptureSuccess(ICoreClientAPI clientApi, EntityAgent byEntity, string fileName)
            {
                _captureInProgress = false;

                // Notify server to transition the loaded plate to Exposed state.
                _owner.ClientChannel?.SendPacket(new CameraCapture.Contracts.PhotoTakenPacket { PhotoId = fileName });

                // Send to server + play sound after capture completes.
                ClientPhotoSyncIntegration.NotifyPhotoCreated(clientApi, fileName);
                clientApi.World.PlaySoundAt(new AssetLocation("game:sounds/effect/woodclick"), byEntity, null, true, 32, 1f);

                _owner.EndViewfinderMode();
            }

            // Cancels hold-still state and exits viewfinder when the scheduled capture fails.
            private void OnCaptureFailure(ICoreClientAPI clientApi, Exception ex)
            {
                _captureInProgress = false;
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
