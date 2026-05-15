# Feature Pillars

This doc defines the mod at a product level. Each pillar maps to one or more `src/Features/<Feature>/` folders.

## Pillars

### 1) Camera Capture

Captures in-engine frames through the wetplate camera workflow and produces photo assets. Covers input, viewfinder state, capture scheduling, image extraction, and the server-side camera and plate updates that finish a shot.

Primary paths:
- [src/Features/CameraCapture/Bridge/](../src/Features/CameraCapture/Bridge) — ModSystem-facing partials, viewfinder state machine, capture gate, hold-still coordinator, effects lookup, server authority.
- [src/Features/CameraCapture/Harmony/](../src/Features/CameraCapture/Harmony) — standalone Harmony patch on `ClientMain.Set3DProjection` for viewfinder zoom.
- [src/Features/CameraCapture/Item/](../src/Features/CameraCapture/Item) — held wetplate camera item.
- [src/Features/CameraCapture/Integration/](../src/Features/CameraCapture/Integration), [Contracts/](../src/Features/CameraCapture/Contracts), [Rendering/](../src/Features/CameraCapture/Rendering)

Flow doc: [FLOW_CAMERA.md](FLOW_CAMERA.md).

### 2) Plate Lifecycle and Chemistry

Moves plate state through sensitization, exposure, development, and final plate stages. Covers process definitions, chemical-step validation, plate-state attributes, and tray or ground progression.

Primary paths:
- [src/Features/PlateLifecycle/Chemistry/](../src/Features/PlateLifecycle/Chemistry)
- [src/Features/PlateLifecycle/State/](../src/Features/PlateLifecycle/State)
- [src/Features/PlateLifecycle/GroundPlate/](../src/Features/PlateLifecycle/GroundPlate)
- [src/Features/PlateLifecycle/BlockEntity/](../src/Features/PlateLifecycle/BlockEntity)
- [src/Features/PlateLifecycle/CameraPlateEligibility.cs](../src/Features/PlateLifecycle/CameraPlateEligibility.cs)
- [src/Features/PlateLifecycle/GroundPlate/GlassPlatePlacement.cs](../src/Features/PlateLifecycle/GroundPlate/GlassPlatePlacement.cs)
- [src/Features/PlateLifecycle/Tray/](../src/Features/PlateLifecycle/Tray)
- [src/Features/PlateLifecycle/Item/](../src/Features/PlateLifecycle/Item)
- [src/Features/PlateBox/](../src/Features/PlateBox)

Flow doc: [FLOW_PLATE_PROCESSING.md](FLOW_PLATE_PROCESSING.md).

### 3) Image Formation and Effects

Shapes the visual character of generated images using configurable wet-plate effects, including the effect pipeline stages and profile selection logic.

Primary paths:
- [src/Features/ImageEffects/Pipeline/](../src/Features/ImageEffects/Pipeline)
- [src/Features/ImageEffects/Profiles/](../src/Features/ImageEffects/Profiles)
- [src/Features/ImageEffects/Integration/](../src/Features/ImageEffects/Integration)
- [src/Features/ImageEffects/Commands/](../src/Features/ImageEffects/Commands)

### 4) Photo Asset Sync and Persistence

Keeps photo files available and consistent across client and server with chunked transport, validation, and multiplayer-safe authorization. Covers upload and download flow, chunk assembly, disk writes, and packet DTOs.

Primary paths:
- [src/Features/PhotoSync/Runtime/](../src/Features/PhotoSync/Runtime)
- [src/Features/PhotoSync/Contracts/](../src/Features/PhotoSync/Contracts)
- [src/Features/PhotoSync/Integration/](../src/Features/PhotoSync/Integration)
- [src/Features/PhotoSync/Storage/](../src/Features/PhotoSync/Storage)

Flow doc: [FLOW_PHOTO_SYNC.md](FLOW_PHOTO_SYNC.md).

### 5) Plate and Photograph Display

Renders photos on photo plates and mounted Frame blocks with cache-aware rebuild behavior and main-thread atlas safety. Covers mesh generation, texture loading and caching, missing-photo recovery, and Frame insert/extract behavior.

Primary paths:
- [src/Features/Frame/Block/](../src/Features/Frame/Block), [src/Features/Frame/BlockEntity/](../src/Features/Frame/BlockEntity) — placed Frame block + single-slot inventory + main-thread mesh build.
- [src/Features/PlateLifecycle/Rendering/](../src/Features/PlateLifecycle/Rendering) — `PhotoPlateRenderUtil.*`, `PhotoMeshUtil`, `PhotoMeshRenderCache`, `PhotoImageProcessor`. Shared by photo plates (held + placed) and the Frame BE.
- [src/Features/PlateLifecycle/Item/ItemPhotoPlate.cs](../src/Features/PlateLifecycle/Item/ItemPhotoPlate.cs) — held photo-plate render path.

Flow doc: [FLOW_PHOTO_DISPLAY.md](FLOW_PHOTO_DISPLAY.md).

Notes:
- Atlas insertion runs on the main thread only; `OnTesselation` may only read cached meshes.
- Pillar 5 consumes `PhotographAttrs` keys defined in pillar 6; it does not own metadata semantics.

### 6) Photograph Metadata and Curation

Owns photo identity, captions, and seen-tracking semantics. Covers metadata keys, photoId and caption normalization, caption authoring UX, server-side validation, and seen-index persistence.

Primary paths:
- [src/Features/PhotoMetadata/Model/](../src/Features/PhotoMetadata/Model)
- [src/Features/PhotoMetadata/Caption/](../src/Features/PhotoMetadata/Caption)
- [src/Features/PhotoMetadata/Runtime/](../src/Features/PhotoMetadata/Runtime)
- [src/Features/PhotoMetadata/Integration/](../src/Features/PhotoMetadata/Integration)

Notes:
- Pillar 4 owns transport / storage mechanics; pillar 6 owns caption + seen + photoId semantics.
- Pillar 5 consumes metadata for rendering; it does not own metadata policy.

### 7) Configuration and Operator Tooling

Exposes tuning and operational controls for players, admins, and local testing. Covers config schema, normalization, commands, and startup wiring.

Primary paths:
- [src/Features/AdminTooling/Config/](../src/Features/AdminTooling/Config)
- [src/Features/AdminTooling/Commands/](../src/Features/AdminTooling/Commands)
- [src/Features/AdminTooling/Startup/](../src/Features/AdminTooling/Startup)
- [src/ModSystem/](../src/ModSystem)

Notes:
- ModSystem lifecycle callbacks remain entrypoints and delegate composition to feature-owned helpers.
- Pillar 7 owns global schema shape, normalization, command surfaces, and startup composition policy.

## Cross-cutting conventions

- Each feature lives under `src/Features/<Feature>/` with common subfolders such as `Bridge/`, `Runtime/`, `Integration/`, `Contracts/`, `Storage/`, `Item/`, `Block/` + `BlockEntity/`, `Rendering/`, `Config/`, `Commands/`, `Startup/`, `Caption/`, and `Harmony/`. Not every feature uses every subfolder.
- Held items, blocks, and block entities owned by a feature live in that feature's folder (e.g. `CameraCapture/Item/ItemWetplateCamera.cs`, `Frame/BlockEntity/BlockEntityFrame.cs`). There is no top-level `src/Items/` or `src/Blocks/`.
- `src/Shared/` holds stateless cross-system helpers only (no domain gameplay).
- `ModSystem.<Side>.cs` partials in `src/ModSystem/` delegate startup and registration into feature bridges; they do not contain feature logic.
- Feature bridge instances are exposed from [`ModSystem.FeatureBridges.cs`](../src/ModSystem/ModSystem.FeatureBridges.cs); callsites use `modSys.<Bridge>.Method()` directly.
- Off-thread I/O dispatches via `TyronThreadPool.QueueTask`; main-thread hops use `sapi.Event.EnqueueMainThreadTask(action, "code")`.
- Atlas insertion / texture resolution runs on the main thread only. `OnTesselation` may only read cached meshes.

## Notes

- A pillar is the high-level feature view; the code may split that work across several systems.
- Example: Plate Lifecycle spans chemistry, state management, tray runtime, and ground-plate runtime.
