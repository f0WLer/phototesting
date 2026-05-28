using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Phototesting.CameraCapture
{
    public sealed class BlockEntityMountedCamera : BlockEntity
    {
        private const string CameraStackAttr = "phototestingMountedCameraStack";
        private const string OwnerUidAttr    = "phototestingMountedCameraOwnerUid";
        private const string FacingYawAttr   = "phototestingMountedFacingYaw";

        private ItemStack? _cameraStack;
        private string _ownerPlayerUid = string.Empty;
        private float _facingYaw;

        public string OwnerPlayerUid => _ownerPlayerUid;

        internal bool HasStoredCamera(IWorldAccessor? world)
        {
            return GetStoredCameraStack(world) != null;
        }

        internal ItemStack? GetStoredCameraStack(IWorldAccessor? world)
        {
            _cameraStack?.ResolveBlockOrItem(world);
            return _cameraStack;
        }

        internal void SetStoredCameraStack(ItemStack cameraStack, string ownerPlayerUid, IWorldAccessor? world)
        {
            _cameraStack = cameraStack.Clone();
            _cameraStack.ResolveBlockOrItem(world);
            _ownerPlayerUid = ownerPlayerUid ?? string.Empty;
            MarkDirty(true);
        }

        internal ItemStack? TakeStoredCameraStack(IWorldAccessor? world)
        {
            ItemStack? stored = _cameraStack?.Clone();
            stored?.ResolveBlockOrItem(world);
            _cameraStack = null;
            _ownerPlayerUid = string.Empty;
            MarkDirty(true);
            return stored;
        }

        internal void MarkCameraDirty()
        {
            MarkDirty(true);
        }

        internal void SetFacingYaw(float yaw)
        {
            _facingYaw = yaw;
            MarkDirty(true);
        }

        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            tessThreadTesselator.TesselateBlock(Block, out MeshData mesh);
            mesh.Rotate(0f, _facingYaw, 0f);
            mesher.AddMeshData(mesh);
            return true;
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);

            if (_cameraStack != null)
                tree.SetItemstack(CameraStackAttr, _cameraStack);
            else
                tree.RemoveAttribute(CameraStackAttr);

            if (string.IsNullOrWhiteSpace(_ownerPlayerUid))
                tree.RemoveAttribute(OwnerUidAttr);
            else
                tree.SetString(OwnerUidAttr, _ownerPlayerUid);

            tree.SetFloat(FacingYawAttr, _facingYaw);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);

            _cameraStack = tree.GetItemstack(CameraStackAttr, null);
            _cameraStack?.ResolveBlockOrItem(worldAccessForResolve);
            _ownerPlayerUid = tree.GetString(OwnerUidAttr, string.Empty);
            _facingYaw = tree.GetFloat(FacingYawAttr, 0f);
        }

        public override void GetBlockInfo(IPlayer forPlayer, System.Text.StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);

            if (_cameraStack?.Collectible?.GetHeldItemName(_cameraStack) is string name && !string.IsNullOrWhiteSpace(name))
                dsc.AppendLine(name);
        }
    }
}