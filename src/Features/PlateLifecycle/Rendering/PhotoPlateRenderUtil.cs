using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Phototesting.AdminTooling;

namespace Phototesting.PlateLifecycle.Rendering
{
    public static partial class PhotoPlateRenderUtil
    {
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

