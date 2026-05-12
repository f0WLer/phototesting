# Photo Sync Flow

Runtime map for photo upload, download, chunk assembly, server-side authorization, and seen/caption metadata.

## Triggers

- A client captures a new photo and needs to upload its bytes to the server.
- A renderer (frame, plate, or held photo item) notices its photo is not on disk locally.
- A client opens a caption dialog or marks a photograph as seen.

## First entry points

- Upload start: `PhotoAssetSync.ClientOnPhotoCreated()` in [src/Features/PhotoSync/Runtime/PhotoAssetSync.Client.cs](../src/Features/PhotoSync/Runtime/PhotoAssetSync.Client.cs)
- Download request start: `PhotoAssetSync.ClientRequestPhotoIfMissing()` in [src/Features/PhotoSync/Runtime/PhotoAssetSync.Client.cs](../src/Features/PhotoSync/Runtime/PhotoAssetSync.Client.cs)
- Server request handling: `PhotoAssetSync.ServerHandleRequest()` in [src/Features/PhotoSync/Runtime/PhotoAssetSync.Server.cs](../src/Features/PhotoSync/Runtime/PhotoAssetSync.Server.cs)
- Server upload assembly: `PhotoAssetSync.ServerHandleChunk()` in [src/Features/PhotoSync/Runtime/PhotoAssetSync.Server.cs](../src/Features/PhotoSync/Runtime/PhotoAssetSync.Server.cs)
- Server caption/seen handlers: [src/Features/PhotoMetadata/Integration/PhotoMetadata.PacketHandlers.cs](../src/Features/PhotoMetadata/Integration/PhotoMetadata.PacketHandlers.cs)

## Upload path (client → server)

1. Camera authority accepts a `PhotoTakenPacket` from the client and calls `PhotoSync.RegisterExpectedUpload(playerUid, photoId)` (see [src/Features/CameraCapture/Bridge/CameraCaptureModSystemBridge.Server.cs](../src/Features/CameraCapture/Bridge/CameraCaptureModSystemBridge.Server.cs)). This whitelists the (player, photoId) pair for `ExpectedUploadTtlMs` (60s).
2. Client capture pipeline calls `PhotoSync.ClientOnPhotoCreated(photoId)`.
3. Client reads the local PNG and streams `PhotoBlobChunkPacket` chunks via `SendChunksConfigured()`.
4. `ServerHandleChunk()` enforces, in order:
   - `ExpectedUploads.IsExpected(playerUid, photoId)` — uploads the player was never authorized for are dropped.
   - On the first chunk only: `ExpectedUploads.TryBeginUpload(playerUid, ServerMaxOpenUploadsPerPlayer)` — caps concurrent uploads per player (config knob, default 2).
   - Chunk assembly into an `IncomingAssembly` keyed by `playerUid|photoId`.
5. On completion: png-signature gate, then `TyronThreadPool.QueueTask` writes bytes to canonical storage off-thread, then hops back to main thread to ack via `PhotoBlobAckPacket` and touch the seen index.
6. Stale or abandoned `IncomingAssembly` entries age out via `ServerPruneTick` (~10s tick, ~120s stale window). Cap slots held by abandoned uploads release with the assembly, so a misbehaving player only locks themselves out.

## Download path (server → client)

1. A renderer (mounted/plate/item) calls `ClientRequestPhotoIfMissing(photoId)`.
2. Client dedupes outstanding requests and sends `PhotoBlobRequestPacket`.
3. `ServerHandleRequest()` enforces `ServerRequestThrottle.TryConsume(playerUid, "request")` — token bucket, 60/min permits, 8 burst capacity. Over-budget requests are silently dropped.
4. Server reads bytes from canonical storage, validates size, streams `PhotoBlobChunkPacket` back.
5. `ClientHandleChunk()` reassembles, writes the PNG to local cache, and notifies waiting renderers (they mark their meshes dirty).

## Caption + seen path

- Caption set: client opens [`GuiDialogPhotographCaption`](../src/Features/PhotoMetadata/Caption/GuiDialogPhotographCaption.cs), sends `PhotoCaptionSetPacket`. Server enforces (see `HandlePhotoMetadataCaptionSetPacket`):
  - Player must be within ~8 blocks of the photograph block position.
  - `world.Claims.TestAccess(player, pos, EnumBlockAccessFlags.BuildOrBreak)` must allow the edit.
  - Caption is byte-capped before persisting via the metadata setter.
- Seen ping: client sends `PhotoSeenPacket` when a photograph mesh becomes visible. Handler validates the photoId via `PhotoMetadataPolicy.NormalizePhotoId` (junk ids cannot create index entries) and touches the seen index. The seen index is bounded by valid photoIds on the server, so no per-player rate limit is needed.
- Seen index persistence is owned by `ServerPhotoSeenService.TryFlush()`, which uses an `Interlocked` in-flight guard and dispatches the actual file write through `TyronThreadPool.QueueTask`.

## State owners

| Concern | Owner |
| --- | --- |
| Chunk assembly, retry dedupe, request gating | `PhotoAssetSync` ([Runtime/](../src/Features/PhotoSync/Runtime)) |
| Per-player request throttle | `PlayerNetworkThrottle` (token bucket, self-pruning) |
| Upload authorization + concurrent-upload cap | `ServerExpectedUploads` |
| Transport DTOs | [`PhotoSyncNetworkPackets`](../src/Features/PhotoSync/Contracts/PhotoSyncNetworkPackets.cs) |
| photoId / file path normalization | [`PhotoAssetStoragePaths`](../src/Features/PhotoSync/Storage/PhotoAssetStoragePaths.cs), [`PhotoMetadataPolicy`](../src/Features/PhotoMetadata/Runtime/PhotoMetadataPolicy.cs) |
| Seen-index persistence | [`ServerPhotoSeenService`](../src/Features/PhotoMetadata/Runtime/ServerPhotoSeenService.cs) |
| Channel + handler wiring | [`PhotoSyncModSystemBridge`](../src/Features/PhotoSync/Integration/PhotoSync.Startup.cs) |
| Server-side caption/seen packet semantics | [`PhotoMetadata.PacketHandlers`](../src/Features/PhotoMetadata/Integration/PhotoMetadata.PacketHandlers.cs) |

## Client / server boundary

- **Client owns**: local PNG cache writes, request/upload dedupe, seen-ping emission, caption dialog UI.
- **Server owns**: canonical photo storage, upload authorization (expected-upload whitelist + per-player concurrent cap), request throttling, caption auth (range + claims + size), seen-index persistence.
- **Trust rule**: server never accepts a `PhotoBlobChunkPacket` for a (player, photoId) it did not authorize via `RegisterExpectedUpload`.

## Where to add X

| Goal | File(s) to edit |
| --- | --- |
| Add a new sync packet type | Define DTO in [`PhotoSyncNetworkPackets.cs`](../src/Features/PhotoSync/Contracts/PhotoSyncNetworkPackets.cs); register in `RegisterPhotoSyncMessageTypes`; wire handler in [`PhotoSync.Startup.cs`](../src/Features/PhotoSync/Integration/PhotoSync.Startup.cs); implement on the runtime partial (Client/Server). |
| Add a new metadata packet (caption/seen-shaped) | DTO in `PhotoSyncNetworkPackets.cs`; register in `RegisterPhotoSyncMessageTypes`; handler in [`PhotoMetadata.PacketHandlers.cs`](../src/Features/PhotoMetadata/Integration/PhotoMetadata.PacketHandlers.cs); wire in [`PhotoSync.SeenAndCaption.cs`](../src/Features/PhotoSync/Integration/PhotoSync.SeenAndCaption.cs). Apply `NormalizePhotoId` and any needed auth (range/claims) before mutating state. |
| Tune throttles or upload cap | Per-player upload cap is `PhotoSyncConfig.ServerMaxOpenUploadsPerPlayer` (admin config). Throttle constants live as private consts at the top of [PhotoAssetSync.cs](../src/Features/PhotoSync/Runtime/PhotoAssetSync.cs) — `RequestPermitsPerMinute`, `RequestBurstCapacity`, `ExpectedUploadTtlMs`. |
| Change photoId or path normalization | [`PhotoAssetStoragePaths`](../src/Features/PhotoSync/Storage/PhotoAssetStoragePaths.cs) for filesystem layout; [`PhotoMetadataPolicy`](../src/Features/PhotoMetadata/Runtime/PhotoMetadataPolicy.cs) for the validation rule reused by every packet handler. |
| Change caption auth (distance, byte cap, claim flag) | `HandlePhotoMetadataCaptionSetPacket` in [`PhotoMetadata.PacketHandlers.cs`](../src/Features/PhotoMetadata/Integration/PhotoMetadata.PacketHandlers.cs). |
| Change persistence side effects on completed upload | `TryPersistUploadedPhoto` in [PhotoAssetSync.Server.cs](../src/Features/PhotoSync/Runtime/PhotoAssetSync.Server.cs). Keep file I/O off the main thread; main-thread hop is via `sapi.Event.EnqueueMainThreadTask`. |
| Change stale-upload pruning | `ServerMaybePruneIncoming` in [PhotoAssetSync.Server.cs](../src/Features/PhotoSync/Runtime/PhotoAssetSync.Server.cs); knobs are `PhotoSyncConfig.ServerPruneIntervalMs` and `ServerUploadStaleMs`. |

## Threading rules (non-obvious)

- Disk I/O for both photo bytes and seen-index flush dispatches through `TyronThreadPool.QueueTask`; never write from a packet handler directly.
- After off-thread work, hop back via `sapi.Event.EnqueueMainThreadTask(action, "code")` before sending packets or touching server state.
- `ServerPhotoSeenService.TryFlush` is single-flight via `Interlocked` and safe to call from any thread; the tick listener calls it every ~10s.