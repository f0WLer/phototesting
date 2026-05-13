using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Phototesting.PlateLifecycle
{
    public abstract class ItemPlateBase : Item
    {
        private const float GroundScale = 2.5f;
        private readonly object _groundMeshLock = new();
        private readonly Dictionary<string, MultiTextureMeshRef> _groundMeshCache = new(StringComparer.OrdinalIgnoreCase);

        private bool TryGetGroundMesh(ICoreClientAPI capi, ItemStack itemstack, out MultiTextureMeshRef? meshRef)
        {
            meshRef = null;
            string code = itemstack?.Collectible?.Code?.ToShortString() ?? Code?.ToShortString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(code)) return false;

            lock (_groundMeshLock)
            {
                if (_groundMeshCache.TryGetValue(code, out var cached) && cached != null)
                {
                    meshRef = cached;
                    return true;
                }
            }

            try
            {
                capi.Tesselator.TesselateItem(this, out MeshData mesh);
                mesh.Scale(new Vec3f(0.5f, 0.5f, 0.5f), GroundScale, GroundScale, GroundScale);
                var uploaded = capi.Render.UploadMultiTextureMesh(mesh);

                lock (_groundMeshLock)
                {
                    _groundMeshCache[code] = uploaded;
                }

                meshRef = uploaded;
                return true;
            }
            catch
            {
                return false;
            }
        }

        // Releases cached ground meshes for this item instance during asset reload/unload.
        private void DisposeGroundMeshCache()
        {
            lock (_groundMeshLock)
            {
                if (_groundMeshCache.Count <= 0) return;

                foreach (MultiTextureMeshRef meshRef in _groundMeshCache.Values)
                {
                    try
                    {
                        meshRef?.Dispose();
                    }
                    catch
                    {
                        // best-effort cache cleanup on unload
                    }
                }

                _groundMeshCache.Clear();
            }
        }

        public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
        {
#pragma warning disable CS0618 // Preserve FP pose behavior on older targets
            base.OnBeforeRender(capi, itemstack, target, ref renderinfo);
#pragma warning restore CS0618

            try
            {
                if (target == EnumItemRenderTarget.Ground)
                {
                    if (TryGetGroundMesh(capi, itemstack, out var meshRef) && meshRef != null)
                    {
                        renderinfo.ModelRef = meshRef;
                    }
                }
            }
            catch
            {
                // ignore
            }
        }

        public override string GetHeldItemName(ItemStack itemStack)
        {
            string fallback = base.GetHeldItemName(itemStack);
            return PlateNameResolver.ResolveDisplayName(api, itemStack, fallback);
        }

        // Suppress the JSON-defined Dry transition once the plate is marked dried,
        // marked never-drying (e.g. bromide), or in a stage that doesn't track wetness.
        public override TransitionableProperties[]? GetTransitionableProperties(IWorldAccessor world, ItemStack itemstack, Vintagestory.API.Common.Entities.Entity forEntity)
        {
            if (itemstack?.Attributes == null) return base.GetTransitionableProperties(world, itemstack, forEntity);
            if (itemstack.Attributes.GetBool(PlateDryingTransition.AttrDried)) return null;
            if (itemstack.Attributes.GetBool(PlateDryingTransition.AttrNeverDries)) return null;
            if (!ShouldTrackDryness(itemstack)) return null;
            return base.GetTransitionableProperties(world, itemstack, forEntity);
        }

        // Apply the plate-box storage multiplier when the stack is currently stored.
        public override float GetTransitionRateMul(IWorldAccessor world, ItemSlot inSlot, EnumTransitionType transType)
        {
            if (transType == EnumTransitionType.Dry
                && inSlot?.Itemstack?.Attributes?.GetBool(PlateDryingTransition.AttrStoredInPlateBox) == true)
            {
                return PlateDryingTransition.ResolveStorageDryingRateMul(api);
            }
            return base.GetTransitionRateMul(world, inSlot, transType);
        }

        // When the Dry transition completes, mark the stack dried in place rather than
        // swapping to a transitioned item — keeps the stack as a dry sensitized/photo plate.
        public override ItemStack OnTransitionNow(ItemSlot slot, TransitionableProperties props)
        {
            if (props?.Type == EnumTransitionType.Dry && slot?.Itemstack != null)
            {
                ItemStack stack = slot.Itemstack.Clone();
                stack.Attributes.SetBool(PlateDryingTransition.AttrDried, true);
                stack.Attributes.RemoveAttribute("transitionstate");
                return stack;
            }
            return base.OnTransitionNow(slot, props);
        }

        // Subclasses opt out of dryness tracking for stages that no longer need it
        // (e.g. a finished photo plate is permanent and doesn't dry).
        protected virtual bool ShouldTrackDryness(ItemStack stack) => true;

        public override void OnUnloaded(ICoreAPI api)
        {
            base.OnUnloaded(api);

            if (api.Side == EnumAppSide.Client)
            {
                DisposeGroundMeshCache();
            }
        }
    }
}
