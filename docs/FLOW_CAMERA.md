# Camera Flow

Runtime map for camera capture interactions. Updated: 2026-05-15.

## First entry points

- Camera item input: `OnHeldInteractStep` in [src/Features/CameraCapture/Item/ItemWetplateCamera.Client.cs](../src/Features/CameraCapture/Item/ItemWetplateCamera.Client.cs); `OnHeldInteractStart` in [src/Features/CameraCapture/Item/ItemWetplateCamera.cs](../src/Features/CameraCapture/Item/ItemWetplateCamera.cs).
- Capture entry from the held item: [src/Features/CameraCapture/Item/ItemWetplateCamera.Client.cs](../src/Features/CameraCapture/Item/ItemWetplateCamera.Client.cs).
- Capture orchestration: `RequestPhotoCaptureFromViewfinder` on the client viewfinder runtime nested in [src/Features/CameraCapture/Bridge/CameraCaptureModSystemBridge.Client.cs](../src/Features/CameraCapture/Bridge/CameraCaptureModSystemBridge.Client.cs).
- Server authority: `OnPhotoTakenReceived` in [src/Features/CameraCapture/Bridge/CameraCaptureModSystemBridge.Server.cs](../src/Features/CameraCapture/Bridge/CameraCaptureModSystemBridge.Server.cs).

## Main runtime path

1. `ItemWetplateCamera.Client.OnHeldInteractStep` detects shutter input while viewfinder is active.
2. `ItemWetplateCamera.Client` resolves the active mod system and calls `CameraCaptureBridge.RequestPhotoCaptureFromViewfinder`.
3. The bridge validates capture preconditions via the [`CaptureGateService`](../src/Features/CameraCapture/Bridge/CameraCaptureModSystemBridge.Client.Viewfinder.CaptureGate.cs) (camera state, loaded plate stage + wetness, hold-still pending), resolves any per-process effects override via [`CaptureEffectsProfileLookup`](../src/Features/CameraCapture/Bridge/CameraCaptureModSystemBridge.Client.Viewfinder.EffectsResolver.cs), then calls `PhotoCaptureRenderer.TryScheduleCapture` ([src/Features/CameraCapture/Rendering/PhotoCaptureRenderer.cs](../src/Features/CameraCapture/Rendering/PhotoCaptureRenderer.cs)).
4. On success the client writes the local PNG, sends `PhotoTakenPacket` to the server, and calls `PhotoAssetSync.ClientOnPhotoCreated` to start the upload (see [FLOW_PHOTO_SYNC.md](FLOW_PHOTO_SYNC.md)). Hold-still scoring continues for the configured duration via [`ViewfinderHoldStillCoordinator`](../src/Features/CameraCapture/Bridge/CameraCaptureModSystemBridge.Client.Viewfinder.HoldStill.cs).
5. `CameraCaptureModSystemBridge.Server.OnPhotoTakenReceived` is the authoritative server entry point. It:
   - Mutates the loaded sensitized plate into an exposed plate.
   - Persists photo metadata onto the plate stack.
   - Calls `PhotoSync.RegisterExpectedUpload(playerUid, photoId)` to whitelist the incoming bytes.

## Viewfinder zoom

- The viewfinder state machine in [`Bridge/CameraCaptureModSystemBridge.Client.Viewfinder.State.cs`](../src/Features/CameraCapture/Bridge/CameraCaptureModSystemBridge.Client.Viewfinder.State.cs) owns enter/exit, depth tracking, and zoom-mechanism selection.
- Preferred zoom: a Harmony transpiler patch on `ClientMain.Set3DProjection(float, float)` lives in the standalone [`ViewfinderZoomHarmony`](../src/Features/CameraCapture/Harmony/ViewfinderZoomHarmony.cs) class. The bridge calls `TryInstall`, `Refresh`, `IsActive`, and `WasScaledRecently` on it; the patch reads viewfinder activation + zoom multiplier from the singleton `PhotoTestingModSystem.ClientInstance`.
- Fallback: client `Settings.Float` keys (`fieldOfView`, `fpHandsFoV`) are mutated in place when the Harmony patch cannot install.

## State owners

| Concern | Owner |
| --- | --- |
| Camera item attributes (loaded plate) | [`ItemWetplateCamera`](../src/Features/CameraCapture/Item/ItemWetplateCamera.cs) |
| Plate item attributes (exposure metadata) | [`PlateStateService`](../src/Features/PlateLifecycle/State/PlateStateService.cs), [`PlateAttrs`](../src/Shared/PlateAttrs.cs) |
| Viewfinder client state machine | [`Client.Viewfinder.State.cs`](../src/Features/CameraCapture/Bridge/CameraCaptureModSystemBridge.Client.Viewfinder.State.cs) |
| Capture gate validation | [`Client.Viewfinder.CaptureGate.cs`](../src/Features/CameraCapture/Bridge/CameraCaptureModSystemBridge.Client.Viewfinder.CaptureGate.cs) |
| Hold-still scoring + final packet send | [`Client.Viewfinder.HoldStill.cs`](../src/Features/CameraCapture/Bridge/CameraCaptureModSystemBridge.Client.Viewfinder.HoldStill.cs) |
| Per-process capture effects override | [`Client.Viewfinder.EffectsResolver.cs`](../src/Features/CameraCapture/Bridge/CameraCaptureModSystemBridge.Client.Viewfinder.EffectsResolver.cs) (`CaptureEffectsProfileLookup`) |
| Viewfinder zoom (Harmony patch) | [`Harmony/ViewfinderZoomHarmony.cs`](../src/Features/CameraCapture/Harmony/ViewfinderZoomHarmony.cs) |
| Capture rendering | [`PhotoCaptureRenderer`](../src/Features/CameraCapture/Rendering/PhotoCaptureRenderer.cs), [`ViewfinderPreviewFrameBuffer`](../src/Features/CameraCapture/Rendering/ViewfinderPreviewFrameBuffer.cs), [`PhotoCropMath`](../src/Features/CameraCapture/Rendering/PhotoCropMath.cs) |
| Server camera authority | [`Bridge/CameraCaptureModSystemBridge.Server.cs`](../src/Features/CameraCapture/Bridge/CameraCaptureModSystemBridge.Server.cs) |
| Channel + handler registration | [`CameraCaptureChannelRegistration`](../src/Features/CameraCapture/Integration/CameraCaptureChannelRegistration.cs) |
| Capture-side packet DTOs | [`PhotoNetworkPackets`](../src/Features/CameraCapture/Contracts/PhotoNetworkPackets.cs) |
| Plate-eligibility checks | [`CameraPlateEligibility`](../src/Features/PlateLifecycle/State/CameraPlateEligibility.cs) |
| Camera-slot resolution shared client/server | [`CameraItemHelper`](../src/Shared/CameraItemHelper.cs) |

## Client / server boundary

- **Client owns**: input handling, viewfinder presentation + zoom, capture scheduling, local PNG write.
- **Server owns**: camera-stack mutation, exposed-plate state transition, expected-upload registration, photo metadata persistence.
- **Trust rule**: the server will never accept upload bytes for a photoId it didn't authorize via `RegisterExpectedUpload`.

## Where to add X

| Goal | File(s) to edit |
| --- | --- |
| Add a capture-side packet | DTO in [`PhotoNetworkPackets.cs`](../src/Features/CameraCapture/Contracts/PhotoNetworkPackets.cs); register in [`CameraCaptureChannelRegistration`](../src/Features/CameraCapture/Integration/CameraCaptureChannelRegistration.cs); handler on the server bridge. |
| Change capture validation gates | [`Client.Viewfinder.CaptureGate.cs`](../src/Features/CameraCapture/Bridge/CameraCaptureModSystemBridge.Client.Viewfinder.CaptureGate.cs) for client-side; `OnPhotoTakenReceived` in [bridge.Server](../src/Features/CameraCapture/Bridge/CameraCaptureModSystemBridge.Server.cs) for authoritative checks. |
| Tune viewfinder / capture pipeline | [`ViewfinderConfig`](../src/Features/CameraCapture/Config/ViewfinderConfig.cs), [`PhotoCapturePipelineConfig`](../src/Features/CameraCapture/Config/PhotoCapturePipelineConfig.cs). |
| Change zoom mechanism | [`ViewfinderZoomHarmony`](../src/Features/CameraCapture/Harmony/ViewfinderZoomHarmony.cs) for the Harmony path; fallback Settings.Float keys live in [`Client.Viewfinder.State.cs`](../src/Features/CameraCapture/Bridge/CameraCaptureModSystemBridge.Client.Viewfinder.State.cs). |
| Add capture-time effect stages | [src/Features/ImageEffects/Pipeline/](../src/Features/ImageEffects/Pipeline) and [Profiles/](../src/Features/ImageEffects/Profiles). |
| Change what the server does on `PhotoTakenPacket` | `OnPhotoTakenReceived` in [bridge.Server](../src/Features/CameraCapture/Bridge/CameraCaptureModSystemBridge.Server.cs). Always call `RegisterExpectedUpload` after acceptance or the upload will be rejected. |

## Related docs

- [FLOW_PHOTO_SYNC.md](FLOW_PHOTO_SYNC.md) — what happens to the bytes after capture.
- [FLOW_PLATE_PROCESSING.md](FLOW_PLATE_PROCESSING.md) — sensitized → exposed plate state transition.
- [FLOW_PHOTO_DISPLAY.md](FLOW_PHOTO_DISPLAY.md) — how exposed/developed plates render once placed in a frame.
