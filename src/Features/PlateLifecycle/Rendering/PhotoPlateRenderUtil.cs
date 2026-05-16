using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Phototesting.AdminTooling;

namespace Phototesting.PlateLifecycle.Rendering
{
    public static partial class PhotoPlateRenderUtil
    {
        /// <summary>Returns the raw movement score from an item stack (0 if absent).</summary>
        public static float ReadMovementScore(ItemStack? itemstack) => GetMovementScore(itemstack);

        /// <summary>Returns an integer bucket for a movement score, suitable for inclusion in cache keys.</summary>
        public static int BucketMovementScore(float movementScore) => QuantizeMovementScore(movementScore);

        /// <summary>
        /// Returns the file path to render for the given photo, generating a motion-artifact derived
        /// file when the movement score is significant.  Returns <paramref name="sourcePath"/> unchanged
        /// when movement is below the effect threshold.
        /// </summary>
        public static string ResolveMovementRenderPath(ICoreClientAPI capi, string sourcePath, string photoFileName, string photoId, float movementScore)
        {
            if (movementScore <= PhotoImageProcessor.MovementEffectMin) return sourcePath;
            int bucket = QuantizeMovementScore(movementScore);
            string tag = $"base-mv{bucket}";
            string derivedPath = GetDerivedPhotoPath(photoFileName, tag);
            return PhotoImageProcessor.TryEnsureDerivedPhoto(capi, sourcePath, derivedPath, $"{photoId}|{tag}", false, 0, 1, movementScore)
                ? derivedPath
                : sourcePath;
        }

        // Resolves effective developer progress for render-stage visuals, clamped to process limits.
        private static void ResolveDevelopedRenderProgress(ICoreClientAPI capi, ItemStack itemstack, out int developPours, out int maxDeveloperPours)
        {
            // Pull process defaults from config so item stacks render with the active process profile.
            ProcessRegistry registry = PhotoTestingConfigAccess.ResolveModSystem(capi)?.Processes ?? new ProcessRegistry();
            PhotographyProcessDefinition process = registry.ResolveOrDefault(PlateStateService.GetProcessId(itemstack));

            maxDeveloperPours = 1;
            foreach (DevelopmentStep step in process.DevelopmentPipeline)
            {
                if (step.ActionKind != DevelopmentActionKind.Developer) continue;
                maxDeveloperPours = step.RequiredApplications;
                break;
            }

            if (maxDeveloperPours < 1) maxDeveloperPours = 1;

            if (PlateStateService.GetStage(itemstack) == PlateStage.Developed || PlateStateService.GetStage(itemstack) == PlateStage.Finished)
            {
                developPours = maxDeveloperPours;
                return;
            }

            if (PlateStateService.GetStage(itemstack) == PlateStage.Developing)
            {
                developPours = PlateDevelopmentService.GetCurrentStepApplications(itemstack);
            }
            else
            {
                developPours = 0;
            }

            // Keep stage-based progress stable for cache keys and derived render variants.
            if (developPours < 0) developPours = 0;
            if (developPours > maxDeveloperPours) developPours = maxDeveloperPours;
        }

    }
}

