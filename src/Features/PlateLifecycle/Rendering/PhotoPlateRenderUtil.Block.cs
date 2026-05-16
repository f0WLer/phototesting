using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Phototesting.PhotoSync.Integration;
using Phototesting.PhotoSync.Storage;

namespace Phototesting.PlateLifecycle.Rendering
{
    public static partial class PhotoPlateRenderUtil
    {
        // Resolves or inserts the block texture atlas entry for a plate photo overlay.
        public static bool TryGetPhotoBlockTexture(ICoreClientAPI capi, ItemStack? itemstack, out TextureAtlasPosition texPos, out float photoAspect, BlockPos? waitingPos = null)
        {
            texPos = capi.BlockTextureAtlas.UnknownTexturePosition;
            photoAspect = 1f;

            if (capi == null || itemstack == null) return false;

            string photoId = itemstack.Attributes?.GetString(PlateAttrs.PhotoId) ?? string.Empty;
            if (string.IsNullOrEmpty(photoId)) return false;

            // Keep server-side photo-seen telemetry up to date while blocks are displayed.
            try
            {
                ClientPhotoSyncIntegration.MaybeSendPhotoSeen(capi, photoId);
            }
            catch (Exception ex)
            {
                    Log.Debug(capi.Logger, "TryGetPhotoBlockTexture photo-seen notification failed: {0}", ex.Message);
            }

            string effectsProfile = string.Empty;
            try
            {
                effectsProfile = itemstack.Collectible?.Attributes?[
                    "photoEffectsProfile"]?.AsString(string.Empty) ?? string.Empty;
            }
            catch
            {
                effectsProfile = string.Empty;
            }

            string photoFileName = PhotoAssetStoragePaths.NormalizePhotoId(photoId);
            if (string.IsNullOrEmpty(photoFileName)) return false;

            int maxDeveloperPours = 1;
            int developPours = maxDeveloperPours;
            if (!string.IsNullOrWhiteSpace(effectsProfile) && effectsProfile.Equals("developed", StringComparison.OrdinalIgnoreCase))
            {
                ResolveDevelopedRenderProgress(capi, itemstack, out developPours, out maxDeveloperPours);
            }

            float movementScore = GetMovementScore(itemstack);
            bool hasMovementEffects = movementScore > PhotoImageProcessor.MovementEffectMin;
            int movementCacheBucket = QuantizeMovementScore(movementScore);

            int versionSnapshot = _meshRenderCache.GetAtlasVersionSnapshot();

            string sourcePath = PhotoAssetStoragePaths.GetPhotoPath(photoFileName);
            if (!File.Exists(sourcePath))
            {
                // Ask PhotoSync for missing assets and remember waiting blocks for refresh.
                try
                {
                    ClientPhotoSyncIntegration.RequestPhotoIfMissing(capi, photoFileName);
                    if (waitingPos != null)
                    {
                        ClientPhotoSyncIntegration.NoteBlockWaitingForPhoto(capi, photoFileName, waitingPos);
                    }
                }
                catch { /* intentional: PhotoSync request is best-effort; missing photo returns false and renders nothing */ }
                return false;
            }

            // Prune stale derived variants before resolving the currently active variant.
            ResolveDerivedRenderPath(capi, photoId, photoFileName, sourcePath, effectsProfile, itemstack,
                developPours, maxDeveloperPours, hasMovementEffects, movementCacheBucket, movementScore,
                out string renderPath, out string renderFileName);


            try
            {
                byte[]? pngBytesForInsert = null;

                bool hasCachedAspect;
                // Reuse aspect data to avoid repeated PNG header/bitmap reads.
                lock (_cacheLock)
                {
                    hasCachedAspect = _blockPhotoAspectCache.TryGetValue(renderPath, out float cachedAspect);
                    if (hasCachedAspect)
                    {
                        photoAspect = cachedAspect;
                    }
                }

                if (!hasCachedAspect)
                {
                    pngBytesForInsert = File.ReadAllBytes(renderPath);

                    photoAspect = 1f;
                    if (PhotoImageProcessor.TryGetPngDimensions(pngBytesForInsert, out int pngW, out int pngH) && pngH > 0)
                    {
                        photoAspect = pngW / (float)pngH;
                    }

                    lock (_cacheLock)
                    {
                        _blockPhotoAspectCache[renderPath] = photoAspect;
                    }
                }

                string photoKey = Path.GetFileNameWithoutExtension(renderFileName);
                AssetLocation texLoc = new AssetLocation("phototesting", $"photo-block-{photoKey}-mv{movementCacheBucket}-v{versionSnapshot}");

                // Lazily create atlas bitmap payload only when cache lookup misses.
                capi.BlockTextureAtlas.GetOrInsertTexture(
                    texLoc,
                    out int _,
                    out texPos,
                    () => capi.Render.BitmapCreateFromPng(pngBytesForInsert ?? File.ReadAllBytes(renderPath)),
                    0.05f
                );
                return texPos != null && texPos != capi.BlockTextureAtlas.UnknownTexturePosition;
            }
            catch (Exception ex)
            {
                capi.Logger.Error("Failed to render tray photo overlay: " + ex);
            }

            return false;
        }
    }
}

