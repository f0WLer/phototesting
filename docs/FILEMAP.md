# Phototesting File Map

Maps major features to folders and key files. Updated: 2026-05-07.

## 1) Main entry points

- [src/ModSystem/ModSystem.cs](../src/ModSystem/ModSystem.cs)
  - Global registration: items, blocks, block entities, packet DTO channel `phototesting`.
- [src/ModSystem/ModSystem.Client.cs](../src/ModSystem/ModSystem.Client.cs)
  - Client lifecycle callback; delegates tooling/config bootstrap and feature startup wiring.
- [src/ModSystem/ModSystem.Server.cs](../src/ModSystem/ModSystem.Server.cs)
  - Server lifecycle callback; same shape as the client side.
- [src/ModSystem/ModSystem.FeatureBridges.cs](../src/ModSystem/ModSystem.FeatureBridges.cs)
  - Holds the per-feature bridge fields (`PhotoSyncBridge`, `CameraCaptureBridge`, etc.) the rest of the mod talks through.

## 2) Feature folders

Each `src/Features/<Feature>/` folder groups related runtime code. Common subfolders include
`Bridge/` (ModSystem-facing partials), `Runtime/` (core logic), `Integration/` (startup and cross-feature wiring),
`Contracts/` (packet DTOs), `Storage/` (filesystem layout), `Item/` (held-item classes),
`Block/` + `BlockEntity/` (placed-block classes), `Rendering/`, `Config/`, `Commands/`,
`Startup/`, `Caption/`, and `Harmony/`. Not every feature uses every subfolder.

### `src/Features/AdminTooling/`

- [Config/](../src/Features/AdminTooling/Config) — root config schema (`PhotoTestingConfig`), feature config models, and load/save helpers.
- [Commands/](../src/Features/AdminTooling/Commands) — `/phototesting` command router and config persistence helpers.
- [Startup/](../src/Features/AdminTooling/Startup) — client/server tooling startup used by `ModSystem.*.cs` lifecycle callbacks.

### `src/Features/CameraCapture/`

- [Bridge/](../src/Features/CameraCapture/Bridge) — `CameraCaptureModSystemBridge.{Integration,Server,Client}.cs` plus client viewfinder partials (`Client.Viewfinder.{State,HoldStill,CaptureGate,EffectsResolver}.cs`). Holds ModSystem-facing wiring, server-side capture handling, and the client viewfinder runtime.
- [Harmony/ViewfinderZoomHarmony.cs](../src/Features/CameraCapture/Harmony/ViewfinderZoomHarmony.cs) — standalone Harmony patch on `ClientMain.Set3DProjection` used as the preferred viewfinder zoom mechanism.
- [Item/](../src/Features/CameraCapture/Item) — `ItemWetplateCamera.{cs,Client.cs,Client.Exposure.cs,Client.Render.cs}` for the held wetplate camera item.
- [Contracts/PhotoNetworkPackets.cs](../src/Features/CameraCapture/Contracts/PhotoNetworkPackets.cs) — capture-side packet DTOs (`PhotoTakenPacket`, capture-config sync).
- [Integration/](../src/Features/CameraCapture/Integration) — channel registration (`CameraCaptureChannelRegistration`).
- [Rendering/](../src/Features/CameraCapture/Rendering) — capture renderer, debug preview, framebuffer queue, crop math.

### `src/Features/PhotoSync/` — see [FLOW_PHOTO_SYNC.md](FLOW_PHOTO_SYNC.md)

- [Runtime/](../src/Features/PhotoSync/Runtime) — `PhotoAssetSync.{cs,Client.cs,Server.cs}` for chunk transport, assembly, and persistence; `PlayerNetworkThrottle` (token-bucket per player) and `ServerExpectedUploads` (upload authorization + open-upload cap) for multiplayer hardening.
- [Contracts/PhotoSyncNetworkPackets.cs](../src/Features/PhotoSync/Contracts/PhotoSyncNetworkPackets.cs) — sync transfer + caption + seen DTOs.
- [Integration/](../src/Features/PhotoSync/Integration) — `PhotoSync.Startup.cs` (channel registration + handler wiring), `PhotoSync.SeenAndCaption.cs` (metadata-side handlers), `ClientPhotoSyncIntegration.cs` (client request/seen hooks).
- [Storage/PhotoAssetStoragePaths.cs](../src/Features/PhotoSync/Storage/PhotoAssetStoragePaths.cs) — canonical photoId normalization + on-disk path resolution.

### `src/Features/PhotoMetadata/`

- [Model/](../src/Features/PhotoMetadata/Model) — `PhotographAttrs` (block-entity attribute keys), `PhotoLastSeenIndex` (seen-timestamp index model).
- [Runtime/PhotoMetadataPolicy.cs](../src/Features/PhotoMetadata/Runtime/PhotoMetadataPolicy.cs) — caption and photoId normalization used by packet handlers.
- [Runtime/ServerPhotoSeenService.cs](../src/Features/PhotoMetadata/Runtime/ServerPhotoSeenService.cs) — seen-index persistence with single-flight `TryFlush` + thread-pool dispatch.
- [Integration/PhotoMetadata.PacketHandlers.cs](../src/Features/PhotoMetadata/Integration/PhotoMetadata.PacketHandlers.cs) — server-side caption/seen packet semantics (range + claim auth, byte cap, photoId validation).
- [Integration/PhotoMetadataCaptionInteractionIntegration.cs](../src/Features/PhotoMetadata/Integration/PhotoMetadataCaptionInteractionIntegration.cs), [PhotoMetadataSeenIntegration.cs](../src/Features/PhotoMetadata/Integration/PhotoMetadataSeenIntegration.cs) — client-side helpers that emit caption/seen packets.
- [Caption/GuiDialogPhotographCaption.cs](../src/Features/PhotoMetadata/Caption/GuiDialogPhotographCaption.cs) — client caption entry dialog.

### `src/Features/Frame/` — see [FLOW_PHOTO_DISPLAY.md](FLOW_PHOTO_DISPLAY.md)

Sole "mounted display" surface in the mod. Holds any photo-bearing item (any stack carrying a non-empty `PhotographAttrs.PhotoId`, e.g. a finished photo plate) and renders the photo on a directional plane.

- [Block/BlockFrame.cs](../src/Features/Frame/Block/BlockFrame.cs) — placeable frame block; routes interaction into the block entity, returns inserted item on break.
- [BlockEntity/BlockEntityFrame.cs](../src/Features/Frame/BlockEntity/BlockEntityFrame.cs) — single-slot inventory + main-thread mesh build via `PhotoPlateRenderUtil` (atlas insertion is not thread-safe; `OnTesselation` only reads cached meshes).

### `src/Features/PlateLifecycle/`

- [Chemistry/](../src/Features/PlateLifecycle/Chemistry) — process registry, sensitization/development steps, chemistry progression services, exposure parameters.
- [State/](../src/Features/PlateLifecycle/State) — plate stage/process attributes and state coordination.
- [GroundPlate/](../src/Features/PlateLifecycle/GroundPlate) — `BlockGlassPlate` partials and interaction helpers.
- [CameraPlateEligibility.cs](../src/Features/PlateLifecycle/CameraPlateEligibility.cs) — shared camera eligibility rules.
- [GroundPlate/GlassPlatePlacement.cs](../src/Features/PlateLifecycle/GroundPlate/GlassPlatePlacement.cs) — item-to-ground-plate placement bridge.
- [BlockEntity/BlockEntityPlateProcessState.cs](../src/Features/PlateLifecycle/BlockEntity/BlockEntityPlateProcessState.cs) — placed glass-plate process state.
- [Item/](../src/Features/PlateLifecycle/Item) — `ItemPlateBase` + `ItemGlassPlate`/`ItemSensitizedPlate`/`ItemPhotoPlate` plus `PlateNameResolver`.
- [Rendering/](../src/Features/PlateLifecycle/Rendering) — `PhotoPlateRenderUtil.{cs,Block.cs,Item.cs,Cache.cs}`, `PhotoMeshUtil`, `PhotoMeshRenderCache`, `PhotoImageProcessor`. Shared photo-on-plate rendering used by plate items, photo plates, and the Frame block entity.
- [Tray/](../src/Features/PlateLifecycle/Tray) — development-tray code (block, blockentity, runtime spec/duration/timed-state, client latch, config).

### `src/Features/PlateBox/`

- [Block/](../src/Features/PlateBox/Block) — placed plate-box block + interaction partials.
- [BlockEntity/](../src/Features/PlateBox/BlockEntity) — plate-box BE state + client lifecycle.
- [Integration/](../src/Features/PlateBox/Integration) — world-mutation helpers.
- [Rendering/](../src/Features/PlateBox/Rendering) — slot renderer + render lifecycle coordinator.

### `src/Features/ImageEffects/`

- [Pipeline/](../src/Features/ImageEffects/Pipeline) — wet-plate effect pipeline + per-stage implementations (tone, grain, halation, sky, etc.).
- [Profiles/](../src/Features/ImageEffects/Profiles) — effect profile model, store, lookup service.
- [Commands/](../src/Features/ImageEffects/Commands) — effects command handler + property mapping.
- [Integration/](../src/Features/ImageEffects/Integration) — pipeline helpers and client seeder.

## 3) `src/Shared/`

Cross-system stateless helpers. Add here only when 2+ systems already use it and there is no domain-gameplay logic.

- [AudioUtils.cs](../src/Shared/AudioUtils.cs) — non-blocking sound playback + pitch variance.
- [BestEffort.cs](../src/Shared/BestEffort.cs) — try/log helper for cleanup paths that must not throw.
- [CameraItemHelper.cs](../src/Shared/CameraItemHelper.cs) — camera-slot resolution + loaded-plate stack rehydration shared client/server.
- [Log.cs](../src/Shared/Log.cs) — prefixed logger entry points.
- [ProcessRegistryLookup.cs](../src/Shared/ProcessRegistryLookup.cs) — process registry lookup helper.
- [WetPlateAttrs.cs](../src/Shared/WetPlateAttrs.cs) — wet-plate attribute key constants.

## 4) Common task lookup

| Goal | Files / folders |
| --- | --- |
| Camera capture authority + viewfinder | [src/Features/CameraCapture/](../src/Features/CameraCapture), [src/Shared/CameraItemHelper.cs](../src/Shared/CameraItemHelper.cs) |
| Viewfinder zoom mechanism | [Harmony/ViewfinderZoomHarmony.cs](../src/Features/CameraCapture/Harmony/ViewfinderZoomHarmony.cs), [Bridge/CameraCaptureModSystemBridge.Client.Viewfinder.State.cs](../src/Features/CameraCapture/Bridge/CameraCaptureModSystemBridge.Client.Viewfinder.State.cs) |
| Plate chemistry / tray progression | [src/Features/PlateLifecycle/](../src/Features/PlateLifecycle), [src/Features/PlateBox/](../src/Features/PlateBox) |
| Photo display (placed) | [src/Features/Frame/](../src/Features/Frame), [src/Features/PlateLifecycle/Rendering/](../src/Features/PlateLifecycle/Rendering) |
| Photograph caption / seen behavior | [src/Features/PhotoMetadata/](../src/Features/PhotoMetadata), [src/Features/PhotoSync/Integration/PhotoSync.SeenAndCaption.cs](../src/Features/PhotoSync/Integration/PhotoSync.SeenAndCaption.cs) |
| Operator config / commands | [src/Features/AdminTooling/](../src/Features/AdminTooling), [src/ModSystem/ModSystem.Client.cs](../src/ModSystem/ModSystem.Client.cs), [src/ModSystem/ModSystem.Server.cs](../src/ModSystem/ModSystem.Server.cs) |
| Photo upload / download issues | [src/Features/PhotoSync/](../src/Features/PhotoSync), see [FLOW_PHOTO_SYNC.md](FLOW_PHOTO_SYNC.md) |
| Image effects / wet-plate look | [src/Features/ImageEffects/](../src/Features/ImageEffects) |

## 5) Audit and flow docs

- [FEATURE_PILLARS.md](FEATURE_PILLARS.md) — high-level feature overview and folder map.
- [FLOW_CAMERA.md](FLOW_CAMERA.md) — camera-capture interaction flow.
- [FLOW_PHOTO_DISPLAY.md](FLOW_PHOTO_DISPLAY.md) — Frame block + plate display path.
- [FLOW_PHOTO_SYNC.md](FLOW_PHOTO_SYNC.md) — client/server photo transfer + chunk handling + auth (current).
- [FLOW_PLATE_PROCESSING.md](FLOW_PLATE_PROCESSING.md) — plate chemistry + progression semantics.
