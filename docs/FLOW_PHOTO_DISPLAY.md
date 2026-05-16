# Photo Display Flow

Runtime map for placed photo display. Updated: 2026-05-07.

## Triggers

- Right-click a placed Frame block while holding a stack carrying a non-empty `PhotographAttrs.PhotoId` to insert it.
- Right-click a populated Frame to retrieve the held stack.
- Render a photo plate item (held or in inventory) — same `PhotoPlateRenderUtil` cache path.

## First entry points

- Frame block class: [src/Features/Frame/Block/BlockFrame.cs](../src/Features/Frame/Block/BlockFrame.cs).
- Frame block entity: [src/Features/Frame/BlockEntity/BlockEntityFrame.cs](../src/Features/Frame/BlockEntity/BlockEntityFrame.cs).
- Photo render helpers: [src/Features/PlateLifecycle/Rendering/PhotoPlateRenderUtil.cs](../src/Features/PlateLifecycle/Rendering/PhotoPlateRenderUtil.cs) (+ `.Block.cs`, `.Item.cs`, `.Cache.cs`).
- Photo plate held-item class: [src/Features/PlateLifecycle/Item/ItemPhotoPlate.cs](../src/Features/PlateLifecycle/Item/ItemPhotoPlate.cs).

## Main runtime path (insert → display)

1. `BlockFrame.OnBlockInteractStart` routes to `BlockEntityFrame.OnInteract`.
2. `BlockEntityFrame.OnInteract` validates the held stack carries a non-empty `PhotographAttrs.PhotoId` and moves it into its single-slot `InventoryGeneric`.
3. `SlotModified` calls `ScheduleMainThreadRebuild` and `MarkDirty(true)`. The mesh build runs on the main thread because atlas insertion is not thread-safe.
4. The next `OnTesselation` (tesselation thread) reads the cached `MeshData` under a lock and adds it to the chunk mesher. If no cache is present yet, it schedules a rebuild and skips this tesselation pass.
5. The mesh build resolves the photo texture via [`PhotoPlateRenderUtil.TryGetPhotoBlockTexture`](../src/Features/PlateLifecycle/Rendering/PhotoPlateRenderUtil.Block.cs), which:
   - Resolves the photo PNG path via [`PhotoAssetStoragePaths`](../src/Features/PhotoSync/Storage/PhotoAssetStoragePaths.cs).
   - Inserts the texture into the block atlas if available locally.
   - Issues a `PhotoSync` request and registers this BE position as waiting if the file is missing.

## Frame asset wiring

- The frame block JSON declares attribute `photoshape` pointing to a companion "photo plane" block code (e.g. `phototesting:photoplanepainting`).
- The plane variant is matched against the frame's facing via the last code part (`north`/`east`/`south`/`west`); the plane shape's `rotateYByType` bakes orientation into the default mesh, which `BlockEntityFrame` clones and UV-stamps with the photo texture.

## Missing-photo subflow

1. The render util detects the referenced photo file is not present locally.
2. It calls [`ClientPhotoSyncIntegration.RequestPhotoIfMissing`](../src/Features/PhotoSync/Integration/ClientPhotoSyncIntegration.cs), normalizing the path/id via [`PhotoAssetStoragePaths`](../src/Features/PhotoSync/Storage/PhotoAssetStoragePaths.cs).
3. The Frame BE is registered as waiting on that photo at its block position.
4. Once `PhotoAssetSync.ClientHandleChunk` writes the file, the waiting BE is re-marked dirty, scheduling a fresh main-thread mesh rebuild.

## State owners

| Concern | Owner |
| --- | --- |
| Frame block class + interaction routing | [`BlockFrame`](../src/Features/Frame/Block/BlockFrame.cs) |
| Frame inventory + main-thread mesh lifecycle | [`BlockEntityFrame`](../src/Features/Frame/BlockEntity/BlockEntityFrame.cs) |
| Photograph attribute keys (PhotoId, Caption) | [`PhotographAttrs`](../src/Features/PhotoSync/Metadata/PhotographAttrs.cs) |
| Photo texture / atlas insertion | [`PhotoPlateRenderUtil`](../src/Features/PlateLifecycle/Rendering/PhotoPlateRenderUtil.cs) (+ `.Block.cs`, `.Item.cs`) |
| Mesh / texture cache lifetime | [`PhotoMeshRenderCache`](../src/Features/PlateLifecycle/Rendering/PhotoMeshRenderCache.cs), [`PhotoPlateRenderUtil.Cache.cs`](../src/Features/PlateLifecycle/Rendering/PhotoPlateRenderUtil.Cache.cs) |
| Image cropping / processing for plate texture | [`PhotoImageProcessor`](../src/Features/PlateLifecycle/Rendering/PhotoImageProcessor.cs) |
| Photo plate held-item rendering | [`ItemPhotoPlate`](../src/Features/PlateLifecycle/Item/ItemPhotoPlate.cs), [`PhotoPlateRenderUtil.Item.cs`](../src/Features/PlateLifecycle/Rendering/PhotoPlateRenderUtil.Item.cs) |
| File arrival / retry behaviour | `PhotoSync` (see [FLOW_PHOTO_SYNC.md](FLOW_PHOTO_SYNC.md)) |

## Threading rule

- Main thread: atlas insertion, texture upload, `MeshData` construction. `BlockEntityFrame.ScheduleMainThreadRebuild` enqueues onto `EnqueueMainThreadTask`.
- Tesselation thread: `OnTesselation` only **reads** cached meshes under `_meshLock`. It must never trigger atlas mutation.

## Client / server boundary

- **Server owns**: block placement, inventory persistence, photo-bearing item attribute persistence on insert.
- **Client owns**: mesh generation, texture loading, atlas insertion, missing-photo request emission.

## Where to add X

| Goal | File(s) to edit |
| --- | --- |
| Change frame insert/extract rules | [`BlockEntityFrame.OnInteract`](../src/Features/Frame/BlockEntity/BlockEntityFrame.cs). |
| Add a new frame variant (different shape / facing) | New asset under `assets/phototesting/blocktypes` + `shapes`; reference its `photoshape` companion plane there. No code change required if the variant follows the existing facing-suffix convention. |
| Change held photo-plate render | [`ItemPhotoPlate`](../src/Features/PlateLifecycle/Item/ItemPhotoPlate.cs) + [`PhotoPlateRenderUtil.Item.cs`](../src/Features/PlateLifecycle/Rendering/PhotoPlateRenderUtil.Item.cs). |
| Change photo-on-plate texture build | [`PhotoPlateRenderUtil.cs`](../src/Features/PlateLifecycle/Rendering/PhotoPlateRenderUtil.cs) and [`PhotoImageProcessor`](../src/Features/PlateLifecycle/Rendering/PhotoImageProcessor.cs). Cache rules live in [`.Cache.cs`](../src/Features/PlateLifecycle/Rendering/PhotoPlateRenderUtil.Cache.cs). |
| Add a new "waiting on missing photo" caller | Use [`ClientPhotoSyncIntegration.NoteBlockWaitingForPhoto`](../src/Features/PhotoSync/Integration/ClientPhotoSyncIntegration.cs) and `RequestPhotoIfMissing`. Always normalize via [`PhotoAssetStoragePaths`](../src/Features/PhotoSync/Storage/PhotoAssetStoragePaths.cs). |

## Related docs

- [FLOW_PHOTO_SYNC.md](FLOW_PHOTO_SYNC.md) — file transport that fills the local cache.
- [FLOW_CAMERA.md](FLOW_CAMERA.md) — where the photo originates.
- [FLOW_PLATE_PROCESSING.md](FLOW_PLATE_PROCESSING.md) — exposed → developed plate progression that produces the displayable stack.
