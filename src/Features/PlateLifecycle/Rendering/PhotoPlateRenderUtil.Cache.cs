using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace Phototesting.PlateLifecycle.Rendering
{
    public static partial class PhotoPlateRenderUtil
    {
        // Shared mesh render cache for the plate render path (item overlay + block overlay).
        private static readonly PhotoMeshRenderCache _meshRenderCache = new();

        // Auxiliary cache lock guards aspect-ratio and prune-state entries only.
        // Mesh cache concurrency is handled internally by PhotoMeshRenderCache.
        private static readonly object _cacheLock = new();
        private static readonly Dictionary<string, float> _blockPhotoAspectCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _derivedPruneState = new(StringComparer.OrdinalIgnoreCase);

        // Disposes cached mesh refs, clears render caches, and bumps versioned cache keys.
        public static int ClearClientRenderCacheAndBumpVersion()
        {
            int cleared = _meshRenderCache.ClearAndBumpVersion();

            lock (_cacheLock)
            {
                _blockPhotoAspectCache.Clear();
                _derivedPruneState.Clear();
            }

            // Clear derived photo cache (best effort)
            try
            {
                string derivedDir = Path.Combine(GamePaths.DataPath, "ModData", "phototesting", "photos", "derived");
                if (Directory.Exists(derivedDir))
                {
                    Directory.Delete(derivedDir, true);
                }
            }
            catch
            {
                // ignore
            }

            return cleared;
        }

        // Builds a deterministic derived image filename for a profile variant.
        private static string GetDerivedPhotoFileName(string photoFileName, string profile)
        {
            string baseName = Path.GetFileNameWithoutExtension(photoFileName);
            return $"{baseName}__{profile}.png";
        }

        // Builds the full on-disk path for a derived image variant.
        private static string GetDerivedPhotoPath(string photoFileName, string profile)
        {
            string derivedFileName = GetDerivedPhotoFileName(photoFileName, profile);
            return Path.Combine(GamePaths.DataPath, "ModData", "phototesting", "photos", "derived", derivedFileName);
        }

        // Best-effort cleanup of obsolete developed-stage variants for a photo.
        private static void MaybePruneObsoleteDevelopedDerived(ICoreClientAPI capi, string photoFileName, ItemStack? itemstack, int developPours, int maxDeveloperPours, bool useDevelopedStage)
        {
            if (string.IsNullOrWhiteSpace(photoFileName) || itemstack?.Attributes == null) return;

            bool isFinishedStage = PlateStateService.GetStage(itemstack) == PlateStage.Finished;

            int keepDevelopedStage = useDevelopedStage ? developPours : 0;
            if (keepDevelopedStage < 0) keepDevelopedStage = 0;
            if (keepDevelopedStage > maxDeveloperPours) keepDevelopedStage = maxDeveloperPours;

            string pruneKey = $"{photoFileName}|{(isFinishedStage ? "finished" : "active")}|{keepDevelopedStage}";
            lock (_cacheLock)
            {
                if (!_derivedPruneState.Add(pruneKey)) return;
            }

            // Pruning is intentionally soft-fail to avoid breaking render paths on IO races.
            try
            {
                string derivedDir = Path.Combine(GamePaths.DataPath, "ModData", "phototesting", "photos", "derived");
                if (!Directory.Exists(derivedDir)) return;

                string baseName = Path.GetFileNameWithoutExtension(photoFileName);
                if (string.IsNullOrWhiteSpace(baseName)) return;

                if (isFinishedStage)
                {
                    for (int stageIndex = 1; stageIndex <= maxDeveloperPours; stageIndex++)
                    {
                        DeleteDerivedDevelopedStageFiles(derivedDir, baseName, stageIndex, maxDeveloperPours);
                    }
                    return;
                }

                if (keepDevelopedStage <= 1) return;

                int previousStage = keepDevelopedStage - 1;
                DeleteDerivedDevelopedStageFiles(derivedDir, baseName, previousStage, maxDeveloperPours);
            }
            catch (Exception ex)
            {
                capi?.Logger?.VerboseDebug($"Phototesting: derived prune skipped for '{photoFileName}': {ex.Message}");
            }
        }

        // Deletes all derived files for a single developed stage index.
        private static void DeleteDerivedDevelopedStageFiles(string derivedDir, string baseName, int stageIndex, int maxDeveloperPours)
        {
            if (stageIndex < 1 || stageIndex > maxDeveloperPours) return;

            string pattern = $"{baseName}__developed{stageIndex}*.png";
            foreach (string filePath in Directory.EnumerateFiles(derivedDir, pattern, SearchOption.TopDirectoryOnly))
            {
                try { File.Delete(filePath); } catch { /* intentional: stale derived file deletion is best-effort; locked or missing files are skipped */ }
            }
        }

        // Reads persisted exposure movement value from an item stack with clamping.
        private static float GetMovementScore(ItemStack? itemstack)
        {
            if (itemstack?.Attributes == null) return 0f;

            try
            {
                double movement = itemstack.Attributes.GetDouble(WetPlateAttrs.HoldStillMovement, 0);
                if (movement <= 0) return 0f;
                if (movement > 1000) movement = 1000;
                return (float)movement;
            }
            catch
            {
                return 0f;
            }
        }

        // Quantizes movement score to a stable integer key segment for cache/variant identity.
        private static int QuantizeMovementScore(float movementScore)
        {
            float clamped = movementScore;
            if (clamped < 0f) clamped = 0f;
            if (clamped > 1000f) clamped = 1000f;
            return (int)Math.Round(clamped * 100f);
        }

        // Resolves the effective render path and filename for a photo, applying derived-stage
        // and motion-artifact variants when needed.  Prunes obsolete derived files before resolving.
        // When no derived variant applies, renderPath == sourcePath and renderFileName == photoFileName.
        private static void ResolveDerivedRenderPath(
            ICoreClientAPI capi,
            string photoId,
            string photoFileName,
            string sourcePath,
            string effectsProfile,
            ItemStack? itemstack,
            int developPours,
            int maxDeveloperPours,
            bool hasMovementEffects,
            int movementCacheBucket,
            float movementScore,
            out string renderPath,
            out string renderFileName)
        {
            renderPath = sourcePath;
            renderFileName = photoFileName;

            bool useDevelopedStage = !string.IsNullOrWhiteSpace(effectsProfile)
                && effectsProfile.Equals("developed", StringComparison.OrdinalIgnoreCase);

            MaybePruneObsoleteDevelopedDerived(capi, photoFileName, itemstack, developPours, maxDeveloperPours, useDevelopedStage);

            if (!useDevelopedStage && !hasMovementEffects) return;

            string profileTag = useDevelopedStage ? $"developed{developPours}" : "base";
            if (hasMovementEffects)
            {
                profileTag = $"{profileTag}-mv{movementCacheBucket}";
            }

            string derivedFileName = GetDerivedPhotoFileName(photoFileName, profileTag);
            string derivedPath = GetDerivedPhotoPath(photoFileName, profileTag);

            if (PhotoImageProcessor.TryEnsureDerivedPhoto(capi, sourcePath, derivedPath, $"{photoId}|{profileTag}", useDevelopedStage, developPours, maxDeveloperPours, movementScore))
            {
                renderPath = derivedPath;
                renderFileName = derivedFileName;
            }
        }
    }
}

