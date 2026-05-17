using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Phototesting.PhotoSync.Metadata;
using Phototesting.PlateLifecycle.Rendering;

namespace Phototesting.Frame
{
    // Photo-frame block entity. Single-slot inventory whose contained item supplies a photo
    // (anything carrying a non-empty PhotographAttrs.PhotoId attribute, e.g. a finished photo plate).
    //
    // Asset wiring: the parent block JSON declares attribute "photoshape" pointing to a companion
    // "photo plane" block code (e.g. "phototesting:photoplanepainting"). The plane variant is matched
    // against the frame's facing via last-code-part suffix (north/east/south/west); the plane shape's
    // rotateYByType bakes orientation into the default mesh, which we clone and UV-stamp.
    //
    // Photo resolution: PhotoPlateRenderUtil.TryGetPhotoBlockTexture handles missing-PNG sync requests
    // and registers this position as waiting. PhotoSync re-marks the BE dirty when the file arrives,
    // which retesselates the mesh on the next frame.
    public class BlockEntityFrame : BlockEntity
    {
        // Inventory persisted under this child tree key.
        private const string InventoryTreeKey = "inventory";

        private readonly InventoryGeneric _inventory;
        public InventoryGeneric Inventory => _inventory;

        // Cached photo mesh — built only on the main thread (atlas insertion is not thread-safe);
        // OnTesselation only reads it.
        private readonly object _meshLock = new object();
        private MeshData? _photoMesh;
        private volatile bool _rebuildScheduled;
        private AssetLocation _photoPlaneCode = new AssetLocation("phototesting", "photoplanepainting-north");
        private int _photoUvRotation = 90;

        public BlockEntityFrame()
        {
            _inventory = new InventoryGeneric(1, "photographframe-0", null, null);
            _inventory.SlotModified += OnSlotModified;
        }

        private void OnSlotModified(int _)
        {
            // SlotModified runs on main thread (interaction handler). Safe to rebuild directly.
            ScheduleMainThreadRebuild();
            MarkDirty(true);
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            _inventory.LateInitialize(_inventory.InventoryID, api);

            string photoshape = Block?.Attributes?["photoshape"]?.AsString("phototesting:photoplanepainting")
                                ?? "phototesting:photoplanepainting";
            string facing = Block?.LastCodePart() ?? "north";
            _photoPlaneCode = new AssetLocation(photoshape + "-" + facing);
            _photoUvRotation = Block?.Attributes?["photoUvRotation"]?.AsInt(90) ?? 90;

            // Initialize runs on the main thread on both sides — kick off an initial mesh build if needed.
            if (api.Side == EnumAppSide.Client && !_inventory[0].Empty)
            {
                ScheduleMainThreadRebuild();
            }
        }

        // Returns true if interaction was handled (insert or remove). Otherwise the base block falls through.
        public bool OnInteract(IWorldAccessor world, IPlayer byPlayer)
        {
            // Take out: any right-click while frame holds an item retrieves it to the player.
            if (!_inventory[0].Empty)
            {
                ItemStack stored = _inventory[0].Itemstack!;
                if (!byPlayer.InventoryManager.TryGiveItemstack(stored))
                {
                    world.SpawnItemEntity(stored, byPlayer.Entity.Pos.XYZ);
                }
                _inventory[0].TakeOutWhole();
                MarkDirty(true);
                return true;
            }

            // Insert: only stacks carrying a non-empty PhotoId attribute (e.g. finished photoplates).
            ItemSlot? heldSlot = byPlayer.InventoryManager.ActiveHotbarSlot;
            ItemStack? held = heldSlot?.Itemstack;
            if (held == null) return false;

            string photoId = held.Attributes?.GetString(PhotographAttrs.PhotoId) ?? string.Empty;
            if (string.IsNullOrEmpty(photoId)) return false;

            int moved = heldSlot!.TryPutInto(world, _inventory[0]);
            if (moved > 0)
            {
                MarkDirty(true);
                return true;
            }
            return false;
        }

        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            bool baseSkip = base.OnTesselation(mesher, tessThreadTesselator);

            if (Api?.Side != EnumAppSide.Client) return baseSkip;
            if (_inventory[0].Empty) return baseSkip;

            MeshData? cached;
            lock (_meshLock) { cached = _photoMesh; }

            if (cached != null)
            {
                mesher.AddMeshData(cached);
            }
            else
            {
                // Mesh not built yet (e.g. first tesselation after chunk load, or photo PNG just arrived).
                // Atlas insertion must happen on the main thread, so schedule a rebuild and let the next
                // MarkDirty cycle pick up the freshly-built mesh.
                ScheduleMainThreadRebuild();
            }

            return baseSkip;
        }

        // Posts a one-shot main-thread task that rebuilds the photo mesh and re-marks the chunk dirty.
        private void ScheduleMainThreadRebuild()
        {
            if (Api is not ICoreClientAPI capi) return;
            if (_rebuildScheduled) return;
            _rebuildScheduled = true;

            capi.Event.EnqueueMainThreadTask(() =>
            {
                _rebuildScheduled = false;
                MeshData? newMesh = BuildPhotoMeshMainThread(capi);
                lock (_meshLock) { _photoMesh = newMesh; }
                if (newMesh != null)
                {
                    MarkDirty(true);
                }
            }, "phototesting-frame-mesh-rebuild");
        }

        // Builds the UV-stamped photo plane mesh on the main thread. Returns null when the photo PNG
        // isn't on disk yet — TryGetPhotoBlockTexture also registers this position as waiting, so
        // PhotoSync will MarkBlockDirty when the file arrives, triggering another tesselation pass.
        private MeshData? BuildPhotoMeshMainThread(ICoreClientAPI capi)
        {
            if (_inventory[0].Empty) return null;

            ItemStack stack = _inventory[0].Itemstack!;

            if (!PhotoPlateRenderUtil.TryGetPhotoBlockTexture(capi, stack, out TextureAtlasPosition photoTex, out float photoAspect, Pos))
            {
                return null;
            }

            Block? planeBlock = Api.World.GetBlock(_photoPlaneCode);
            if (planeBlock == null) return null;

            MeshData? baseMesh = capi.TesselatorManager.GetDefaultBlockMesh(planeBlock);
            if (baseMesh == null) return null;

            // Clone before stamping UVs so the cached default mesh stays untouched.
            MeshData cloned = baseMesh.Clone();
            PhotoMeshUtil.StampUvByRotationCropped(cloned, photoTex, _photoUvRotation, photoAspect, PhotoMeshUtil.PhotoTargetAspect);
            return cloned;
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);

            ITreeAttribute? invTree = tree.GetTreeAttribute(InventoryTreeKey);
            if (invTree != null)
            {
                _inventory.FromTreeAttributes(invTree);
            }

            // Slot contents may have changed during sync — drop cached mesh and let the next
            // OnTesselation schedule a fresh build on the main thread.
            lock (_meshLock) { _photoMesh = null; }
            if (Api?.Side == EnumAppSide.Client && !_inventory[0].Empty)
            {
                ScheduleMainThreadRebuild();
            }
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);

            TreeAttribute invTree = new TreeAttribute();
            _inventory.ToTreeAttributes(invTree);
            tree[InventoryTreeKey] = invTree;
        }

        // Release the cloned mesh's vertex/index buffers when the chunk unloads or the block is broken.
        // The mesh is rebuilt on demand from the cached default plane mesh, so dropping it is free.
        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            lock (_meshLock) { _photoMesh = null; }
        }

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();
            lock (_meshLock) { _photoMesh = null; }
        }
    }
}
