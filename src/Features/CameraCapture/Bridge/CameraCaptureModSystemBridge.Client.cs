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
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

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
            CameraCaptureChannelRegistration.ConfigureClientHandlers(ClientChannel, OnPhotoCaptureConfigReceived, OnMountedCameraControlReceived);
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
            _virtualExposureRenderer.ExposurePreviewSink = _virtualCameraPreviewRenderer;
            _virtualCameraPreviewRenderer.ExposureRenderer = _virtualExposureRenderer;
            api.Event.RegisterRenderer(_virtualExposureRenderer, EnumRenderStage.Before, "phototesting-virtualexposure");

            _debugPreviewRenderer = new ViewfinderDebugPreviewRenderer(api, _virtualCameraPreviewRenderer);
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

        private void OnMountedCameraControlReceived(MountedCameraControlPacket packet)
        {
            if (packet == null) return;
            CaptureClientRuntime.ApplyMountedExposureControl(packet);
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

        // Seals the partial exposure for the given ExposurePaused plate client-side and sends a combined
        // seal+insert packet to the server. Returns true when the packet was sent successfully.
        internal bool TrySendSealForTray(ICoreClientAPI capi, BlockPos trayPos, ItemStack trayPlate)
        {
            if (ClientChannel == null) return false;

            string exposureId = trayPlate.Attributes?.GetString(PlateStateAttributes.ExposureId) ?? string.Empty;
            if (string.IsNullOrEmpty(exposureId)) return false;

            string processId = PlateStateService.GetProcessId(trayPlate);
            if (!PlateProcessProfile.TryParse(processId, out PlateProcessProfile profile))
                profile = PlateProcessProfile.Iodide;

            // Tray development must match normal export policy: same target exposure, output size, and effects resolution.
            int targetFrames = Math.Max(1, trayPlate.Attributes?.GetInt(PlateStateAttributes.ExposureTargetFrames) ?? profile.SampleCount);
            int maxDimension = PhotoTestingConfigAccess.ResolveClientConfig(capi)?.Viewfinder?.PhotoCaptureMaxDimension
                ?? ViewfinderConfig.DefaultPhotoCaptureMaxDimension;
            WetplateEffectsConfig baselineEffects = ImageEffectsPipelineBridge.LoadCaptureBaseline(capi);
            WetplateEffectsConfig? effectsOverride = CaptureEffectsProfileLookup.ResolveForLoadedPlate(this, trayPlate);

            string? photoId = PartialExposureSealer.SealToPng(
                exposureId,
                capi,
                profile,
                targetFrames,
                maxDimension,
                baselineEffects,
                effectsOverride);
            if (string.IsNullOrEmpty(photoId)) return false;

            ClientPhotoSyncIntegration.NotifyPhotoCreated(capi, photoId);

            ClientChannel.SendPacket(new SealAndInsertIntoTrayPacket
            {
                ExposureId = exposureId,
                PhotoId    = photoId,
                TrayPosX   = trayPos.X,
                TrayPosY   = trayPos.Y,
                TrayPosZ   = trayPos.Z,
                TrayPosDim = trayPos.dimension,
            });
            return true;
        }

    // Stateful client-side runtime for viewfinder input and shutter capture scheduling.
    // Keeps per-tick input state and capture lifecycle transitions outside the mod-system partial surface.

        private CameraCaptureClientRuntime? _cameraCaptureClientRuntime;
        private CameraCaptureClientRuntime CaptureClientRuntime => _cameraCaptureClientRuntime ??= new CameraCaptureClientRuntime(this);

        // Starts or toggles a fixed-position virtual accumulation exposure for a mounted (tripod-attached) camera.
        internal bool RequestMountedPhotoCapture(EntityAgent byEntity, bool silentIfBusy = false, ExposureStartOptions startOptions = default)
        {
            return CaptureClientRuntime.TryToggleMountedExposure(silentIfBusy, startOptions);
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

            private string _mountedExposureId = string.Empty;
            private ItemStack? _mountedCameraStackSnapshot;
            private VirtualCameraState? _pendingMountedCameraState;
            private ExposureStartOptions _pendingMountedStartOptions;

            private bool _suppressViewfinderUntilRmbReleased;
            private float _rmbUpSeconds;
            private bool _lastRmbDown;
            private bool _lastLmbDown;
            private bool _rightMouseDown;
            private MouseEventDelegate? _mouseDownHandler;
            private MouseEventDelegate? _mouseUpHandler;
            private MouseWheelEventDelegate? _mouseWheelHandler;
            private KeyEventDelegate? _keyDownHandler;
            private long _lastShutterGateChatMs;

            private GuiDialogCameraTimer? _timerDialog;

            internal CameraCaptureClientRuntime(CameraCaptureModSystemBridge owner)
            {
                _owner = owner;
            }

            // Subscribes to MouseDown/MouseUp so RMB state is event-driven (no Input polling / no try-catch).
            // Also subscribes scroll-wheel and +/- keys for live viewfinder zoom.
            internal void SubscribeMouseEvents(ICoreClientAPI api)
            {
                _mouseDownHandler = (MouseEvent e) => { if (e.Button == EnumMouseButton.Right) _rightMouseDown = true; };
                _mouseUpHandler   = (MouseEvent e) => { if (e.Button == EnumMouseButton.Right) _rightMouseDown = false; };
                api.Event.MouseDown += _mouseDownHandler;
                api.Event.MouseUp   += _mouseUpHandler;

                _mouseWheelHandler = (MouseWheelEventArgs e) =>
                {
                    if (!_owner.IsViewfinderActive) return;
                    float delta = -e.deltaPrecise * CameraCaptureModSystemBridge.ZoomFovStepRad;
                    _owner.AdjustViewfinderZoom(delta);
                    e.SetHandled();
                };
                api.Event.MouseWheelMove += _mouseWheelHandler;

                _keyDownHandler = (KeyEvent e) =>
                {
                    bool minus = e.KeyCode == (int)GlKeys.Minus    || e.KeyCode == (int)GlKeys.KeypadMinus;
                    bool plus  = e.KeyCode == (int)GlKeys.Plus     || e.KeyCode == (int)GlKeys.KeypadPlus;

                    // Shift+Plus opens the timer-camera settings dialog.
                    if (plus)
                    {
                        bool shiftHeld = api.Input.KeyboardKeyStateRaw[(int)GlKeys.ShiftLeft]
                                      || api.Input.KeyboardKeyStateRaw[(int)GlKeys.RShift];
                        if (shiftHeld)
                        {
                            ItemStack? camStack = CameraItemHelper.GetActiveCameraStack(api);
                            if (camStack?.Item is ItemWetplateCameraTimer)
                            {
                                _timerDialog ??= new GuiDialogCameraTimer(api, _owner.ClientChannel!);
                                _timerDialog.OpenFor(camStack);
                                e.Handled = true;
                                return;
                            }
                        }
                    }

                    if (!_owner.IsViewfinderActive) return;
                    if (!minus && !plus) return;
                    _owner.AdjustViewfinderZoom(plus ? -CameraCaptureModSystemBridge.ZoomFovStepRad
                                                     :  CameraCaptureModSystemBridge.ZoomFovStepRad);
                    e.Handled = true;
                };
                api.Event.KeyDown += _keyDownHandler;
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
                if (_mouseWheelHandler != null)
                {
                    var w = _mouseWheelHandler;
                    BestEffort.Try(_owner.BestEffortLogger, "unsubscribe viewfinder mousewheel", () => api.Event.MouseWheelMove -= w);
                    _mouseWheelHandler = null;
                }
                if (_keyDownHandler != null)
                {
                    var k = _keyDownHandler;
                    BestEffort.Try(_owner.BestEffortLogger, "unsubscribe viewfinder keydown", () => api.Event.KeyDown -= k);
                    _keyDownHandler = null;
                }
                _rightMouseDown = false;
            }

            internal void OnClientViewfinderTick(float dt)
            {
                if (_owner.ClientApi == null) return;

                // Mounted exposure auto-halt: persist accumulated buffer and pause the plate so it can be resumed later.
                if (!string.IsNullOrEmpty(_mountedExposureId) && _owner._virtualExposureRenderer?.State == ExposureState.Done)
                    PersistPartialMountedExposure();

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
                    ItemStack? cameraStack = activeCameraSlot?.Itemstack;

                    bool cameraLoaded = CameraCaptureModSystemBridge.CameraHasLoadedPlate(cameraStack);

                    // Load: accept consolidated sensitized plates.
                    if (!cameraLoaded)
                    {
                        if (offhand != null && offhand.Empty && CameraItemHelper.HasMountedTripod(cameraStack))
                        {
                            _owner.ClientChannel.SendPacket(new CameraTripodPacket { Mount = false });
                            return;
                        }

                        if (CameraItemHelper.IsTripodItemStack(offstack) && !CameraItemHelper.HasMountedTripod(cameraStack))
                        {
                            _owner.ClientChannel.SendPacket(new CameraTripodPacket { Mount = true });
                            return;
                        }

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
                    if (_rmbUpSeconds > RmbReleaseGraceSeconds)
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
                    {
                        ExportAndSealExposure(byEntity);
                    }
                    else
                    {
                        if (acc is ViewportExposureAccumulator viewportAcc
                            && acc.FramesAccumulated > 0
                            && !string.IsNullOrEmpty(_owner.ActiveExposureId))
                        {
                            byte[]? blob = viewportAcc.ExportPartial();
                            if (blob != null)
                                ExposureAccumulationStore.Save(_owner.ActiveExposureId, blob);
                        }

                        SendExposureStatePacket(isExposing: false, acc.FramesAccumulated, _owner.ActiveExposureId, acc.TargetFrames);
                    }

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
                    SendExposureStatePacket(isExposing: true, existingAcc.FramesAccumulated, exposureId, existingAcc.TargetFrames);
                    return true;
                }

                // Fresh exposure: generate a new session ID and allocate a new accumulator.
                // Exception: if the plate already carries an exposure ID with a saved partial blob
                // (e.g. transferred mid-exposure from a mounted camera), keep that ID so the
                // accumulated frames are restored and the plate's ID doesn't get orphaned.
                byte[]? crossCameraBlob = null;
                if (!string.IsNullOrEmpty(exposureId) &&
                    ExposureAccumulationStore.TryLoad(exposureId, out byte[]? storedBlob) &&
                    storedBlob != null)
                {
                    crossCameraBlob = storedBlob;
                    // Keep exposureId — do NOT generate a new one.
                }
                else
                {
                    exposureId = Guid.NewGuid().ToString("N");
                }

                string processId = PlateStateService.GetProcessId(loadedPlateStack);
                if (!PlateProcessProfile.TryParse(processId, out PlateProcessProfile profile))
                    profile = PlateProcessProfile.Iodide;

                // Consume the primed accumulator prepared at viewfinder entry, or allocate a fresh one.
                // When Prime() was called, the PBO ring is already warm so the first sample tick maps
                // a real frame immediately — no sync GL.ReadPixels stall, no 2-kick priming gap.
                ViewportExposureAccumulator newAcc = _owner._primedViewportAccumulator ?? new ViewportExposureAccumulator(clientApi);
                _owner._primedViewportAccumulator = null;
                newAcc.OnAutoHalt = () => OnAccumulatorAutoHalt(byEntity, newAcc, exposureId);
                newAcc.Start(profile, startOptions);

                // Restore accumulated frames from a prior session (cross-camera resume).
                if (crossCameraBlob != null)
                    newAcc.PrimeFromPartial(crossCameraBlob);

                ViewfinderExposureRegistry.Register(exposureId, newAcc);
                _owner.ActiveAccumulator = newAcc;
                _owner.ActiveExposureId = exposureId;

                _owner.MaybeShowF4GuiLessTip();
                SendExposureStatePacket(isExposing: true, newAcc.FramesAccumulated, exposureId, newAcc.TargetFrames);

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
                    {
                        ExposureAccumulationStore.Delete(exposureId);
                        ViewfinderExposureRegistry.Remove(exposureId);
                    }
                }
                catch (Exception ex)
                {
                    _owner.ClientApi?.Logger.Error("Phototesting: accumulation export failed — " + ex);
                }
                finally
                {
                    _owner.ActiveAccumulator = null;
                    _owner.ActiveExposureId = string.Empty;
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

            // Spawns the camera-mounted block at the player's current position and moves the camera item into it.
            // Snapshots the player's eye pose so the renderer uses it when the exposure later begins.
            // The exposure itself does not start here — the player must right-click the spawned block to begin accumulation.
            internal bool TryToggleMountedExposure(bool silentIfBusy, ExposureStartOptions startOptions = default)
            {
                var renderer = _owner._virtualExposureRenderer;
                if (renderer == null) return false;

                // While actively capturing the camera is in the block entity, not in the player's hand.
                if (renderer.State == ExposureState.Capturing) return true;

                // Guard: don't overwrite a completed but not-yet-exported session.
                if (renderer.State == ExposureState.Done) return true;

                if (!CameraCaptureModSystemBridge.CaptureGateService.TryValidateCaptureRequest(
                    _owner, silentIfBusy, isMounted: false, out ItemStack? loadedPlateStack)) return false;

                var clientApi = _owner.ClientApi;
                if (clientApi == null) return false;

                // Refresh snapshot so export always uses the current plate data.
                _mountedCameraStackSnapshot = CameraItemHelper.GetActiveCameraStack(clientApi)?.Clone();

                // Snapshot the player's eye position now so any newly mounted block can restart the
                // renderer later, even after the client relaunches and the renderer instance is gone.
                var player = clientApi.World.Player;
                var sidedPos = player.Entity.Pos;
                _pendingMountedCameraState = new VirtualCameraState(
                    sidedPos.XYZ.AddCopy(0, player.Entity.LocalEyePos.Y, 0),
                    sidedPos.Yaw,
                    sidedPos.Pitch,
                    ((ClientMain)clientApi.World).MainCamera.Fov,
                    sidedPos.Dimension,
                    selfPortrait: true);
                _pendingMountedStartOptions = startOptions;

                string newPlateExposureId = loadedPlateStack?.Attributes?.GetString(PlateStateAttributes.ExposureId) ?? string.Empty;
                bool resumingSamePlate = renderer.State == ExposureState.Paused
                    && !string.IsNullOrEmpty(_mountedExposureId)
                    && string.Equals(newPlateExposureId, _mountedExposureId, StringComparison.Ordinal);

                if (!resumingSamePlate)
                {
                    // Different plate — persist any paused partial so it can be resumed when that plate is reloaded.
                    if (renderer.State == ExposureState.Paused)
                        PersistPartialMountedExposure();
                    _mountedExposureId = !string.IsNullOrEmpty(newPlateExposureId) ? newPlateExposureId : Guid.NewGuid().ToString("N");
                }

                // Ask the server to spawn the camera-mounted block and move the camera item into it.
                // The exposure will not begin until the player right-clicks the spawned block.
                _owner.ClientChannel?.SendPacket(CreateCameraMountRequest(_pendingMountedCameraState.Value, _pendingMountedStartOptions));
                return true;
            }

            internal void ApplyMountedExposureControl(MountedCameraControlPacket packet)
            {
                var renderer = _owner._virtualExposureRenderer;
                if (renderer == null || packet == null) return;

                if (!string.IsNullOrEmpty(packet.ExposureId))
                    _mountedExposureId = packet.ExposureId;
                else if (string.IsNullOrEmpty(_mountedExposureId) || (packet.IsExposing && renderer.State != ExposureState.Paused))
                    // Either no ID at all (first mount), or a fresh-start packet arrived with no ID from
                    // the server (defensive: prevents inheriting a stale ID from a previous camera session).
                    _mountedExposureId = Guid.NewGuid().ToString("N");

                if (packet.HasCameraState)
                {
                    _pendingMountedCameraState = new VirtualCameraState(
                        new Vec3d(packet.CameraPosX, packet.CameraPosY, packet.CameraPosZ),
                        packet.CameraYaw,
                        packet.CameraPitch,
                        packet.CameraFov,
                        packet.CameraDimension,
                        selfPortrait: true);
                    _pendingMountedStartOptions = ExposureStartOptions.FromStopModeInt(packet.StopMode, packet.StopAfterSeconds);

                    var clientApi = _owner.ClientApi;
                    if (clientApi != null)
                    {
                        PlateProcessProfile previewProcess = ResolveMountedPlateProcessProfile(clientApi, packet.ProcessId);

                        // Keep idle and live mounted preview chemistry aligned with the active plate.
                        if (_owner._virtualCameraPreviewRenderer != null)
                            _owner._virtualCameraPreviewRenderer.EmulsionProcess = previewProcess;
                    }
                }

                if (packet.PrepareIdlePreview)
                {
                    if (_pendingMountedCameraState is VirtualCameraState idleCameraState)
                        renderer.PrepareCamera(idleCameraState);
                }
                else if (!packet.IsExposing)
                {
                    renderer.ClearCamera();
                }

                if (packet.IsExposing)
                {
                    if (renderer.State == ExposureState.Paused)
                    {
                        renderer.Resume();
                        SendExposureStatePacket(true, renderer.FramesAccumulated, _mountedExposureId, renderer.CapFrameCount);
                        return;
                    }

                    // Fresh start: use the positional snapshot taken when the camera was mounted via LMB.
                    if (_pendingMountedCameraState is VirtualCameraState cameraState)
                    {
                        var clientApi = _owner.ClientApi;
                        if (clientApi == null) return;

                        PlateProcessProfile profile = ResolveMountedPlateProcessProfile(clientApi, packet.ProcessId);

                        renderer.ApplyFinishing = false;
                        renderer.ExposurePreviewSink = _owner._virtualCameraPreviewRenderer;
                        renderer.Start(cameraState, profile, _pendingMountedStartOptions);

                        // Restore a previously saved partial accumulation if one exists for this plate's exposure.
                        if (!string.IsNullOrEmpty(_mountedExposureId) &&
                            ExposureAccumulationStore.TryLoad(_mountedExposureId, out byte[]? partialData) &&
                            partialData != null)
                        {
                            renderer.PrimeFromPartial(partialData);
                        }

                        SendExposureStatePacket(true, renderer.FramesAccumulated, _mountedExposureId, renderer.CapFrameCount);
                    }
                    return;
                }

                if (renderer.State != ExposureState.Capturing) return;

                renderer.Pause();

                if (renderer.FramesAccumulated > 0 && !string.IsNullOrEmpty(_mountedExposureId))
                {
                    byte[]? blob = renderer.ExportPartial();
                    if (blob != null)
                        ExposureAccumulationStore.Save(_mountedExposureId, blob);
                }

                SendExposureStatePacket(false, renderer.FramesAccumulated, _mountedExposureId, renderer.CapFrameCount);
            }

            // Resolves the active wet-plate chemistry for mounted preview/exposure from the packet when present,
            // otherwise from the currently loaded plate snapshot in the mounted camera item.
            private PlateProcessProfile ResolveMountedPlateProcessProfile(ICoreClientAPI clientApi, string? packetProcessId)
            {
                CameraItemHelper.TryGetLoadedPlateStack(_mountedCameraStackSnapshot, clientApi.World, out ItemStack? loadedPlate);

                string processId = !string.IsNullOrEmpty(packetProcessId)
                    ? packetProcessId
                    : PlateStateService.GetProcessId(loadedPlate);

                if (!PlateProcessProfile.TryParse(processId, out PlateProcessProfile profile))
                    profile = PlateProcessProfile.Iodide;

                return profile;
            }

            // Persists the accumulated exposure buffer to disk and sets the plate to ExposurePaused.
            // Called when the renderer auto-stops (timer elapsed or target samples reached).
            // The mount state fields are intentionally preserved so the player can right-click the
            // block to immediately resume from the saved partial without needing to remount.
            private void PersistPartialMountedExposure()
            {
                var renderer = _owner._virtualExposureRenderer;
                if (renderer == null) return;

                if (renderer.FramesAccumulated > 0 && !string.IsNullOrEmpty(_mountedExposureId))
                {
                    byte[]? blob = renderer.ExportPartial();
                    if (blob != null)
                        ExposureAccumulationStore.Save(_mountedExposureId, blob);
                }

                // Tell the server to set the plate to ExposurePaused with the current frame count.
                SendExposureStatePacket(false, renderer.FramesAccumulated, _mountedExposureId, renderer.CapFrameCount);

                renderer.Discard();
                // Intentionally keep _mountedExposureId, _mountedCameraStackSnapshot, and
                // _pendingMountedCameraState so the player can right-click the block to resume
                // without dismounting and remounting.
            }

            private static CameraMountRequestPacket CreateCameraMountRequest(in VirtualCameraState cameraState, ExposureStartOptions startOptions)
            {
                return new CameraMountRequestPacket
                {
                    CameraPosX = cameraState.Position.X,
                    CameraPosY = cameraState.Position.Y,
                    CameraPosZ = cameraState.Position.Z,
                    CameraYaw = cameraState.Yaw,
                    CameraPitch = cameraState.Pitch,
                    CameraFov = cameraState.Fov,
                    CameraDimension = cameraState.Dimension,
                    StopMode = (int)startOptions.StopMode,
                    StopAfterSeconds = startOptions.StopAfterSeconds
                };
            }

        }
}
