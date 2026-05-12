using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Phototesting.AdminTooling;

namespace Phototesting
{
    // Wet-plate timer and capture-metadata attribute keys/helpers.
    // Provides shared read/write utilities so client and server treat plate attrs consistently.
    public static class WetPlateAttrs
    {
        public const string WetCreatedTotalHours = "phototestingWetCreatedTotalHours";
        public const string WetDurationHours = "phototestingWetDurationHours";
        public const string StoredRemainingWetHours = "phototestingStoredRemainingWetHours";
        public const string PhotoId = "photoId";
        public const string HoldStillSeconds = "phototestingHoldStillSeconds";
        public const string HoldStillMovement = "phototestingHoldStillMovement";

        // Resolves configured wet lifetime with a safe fallback when config is unavailable.
        public static double ResolveWetDurationHours(ICoreAPI? api)
        {
            return PhotoTestingConfigAccess.ResolveConfig(api)?.PlateProcessing?.WetPlateDurationHours
                ?? 0.66;
        }

        // Returns remaining wet hours from live timer state or persisted storage fallback.
        public static double GetRemainingWetHours(IWorldAccessor world, ItemStack stack)
        {
            if (stack?.Attributes == null) return 0;

            if (world?.Calendar == null)
            {
                return Math.Max(0, stack.Attributes.GetDouble(StoredRemainingWetHours, 0));
            }

            double created = stack.Attributes.GetDouble(WetCreatedTotalHours, -1);
            double duration = stack.Attributes.GetDouble(WetDurationHours, 0);
            if (created < 0 || duration <= 0)
            {
                return Math.Max(0, stack.Attributes.GetDouble(StoredRemainingWetHours, 0));
            }

            double elapsed = world.Calendar.TotalHours - created;
            return Math.Max(0, duration - elapsed);
        }

        // Returns true when the wet timer has fully elapsed (including stored timer state).
        public static bool IsDry(IWorldAccessor world, ItemStack stack)
        {
            if (stack?.Attributes == null) return false;

            double stored = stack.Attributes.GetDouble(StoredRemainingWetHours, -1);
            if (stored >= 0) return stored <= 0;

            double duration = stack.Attributes.GetDouble(WetDurationHours, 0);
            if (duration <= 0) return false;

            return GetRemainingWetHours(world, stack) <= 0;
        }

        // Ensures wet timer attributes are initialized or resumed for a newly sensitized plate.
        public static void EnsureWetTimer(IWorldAccessor world, ItemStack stack, double durationHours)
        {
            if (world?.Calendar == null || stack?.Attributes == null) return;

            double storedRemaining = stack.Attributes.GetDouble(StoredRemainingWetHours, -1);
            if (storedRemaining >= 0)
            {
                ResumeWetTimerFromStorage(world, stack);
                return;
            }

            if (stack.Attributes.GetDouble(WetCreatedTotalHours, -1) < 0)
            {
                stack.Attributes.SetDouble(WetCreatedTotalHours, world.Calendar.TotalHours);
            }

            if (stack.Attributes.GetDouble(WetDurationHours, 0) <= 0)
            {
                stack.Attributes.SetDouble(WetDurationHours, durationHours);
            }
        }

        // Resets wet timer attributes to a fresh duration starting at current world time.
        public static void ResetWetTimer(IWorldAccessor world, ItemStack stack, double durationHours)
        {
            if (world?.Calendar == null || stack?.Attributes == null) return;

            stack.Attributes.RemoveAttribute(StoredRemainingWetHours);
            stack.Attributes.SetDouble(WetCreatedTotalHours, world.Calendar.TotalHours);
            stack.Attributes.SetDouble(WetDurationHours, durationHours);
        }

        // Removes all wet timer attributes from a plate stack.
        public static void ClearWetTimer(ItemStack stack)
        {
            if (stack?.Attributes == null) return;

            stack.Attributes.RemoveAttribute(StoredRemainingWetHours);
            stack.Attributes.RemoveAttribute(WetCreatedTotalHours);
            stack.Attributes.RemoveAttribute(WetDurationHours);
        }

        // Appends localized wet/dry tooltip text derived from current timer state.
        public static void AppendWetnessInfo(IWorldAccessor world, ItemStack stack, System.Text.StringBuilder dsc)
        {
            if (stack == null || dsc == null) return;

            double hoursLeft = GetRemainingWetHours(world, stack);
            if (hoursLeft > 0)
            {
                int minutesLeft = (int)Math.Ceiling(hoursLeft * 60);
                dsc.AppendLine(string.Format(Lang.Get("phototesting:wetplate-wetness"), minutesLeft));
            }
            else
            {
                dsc.AppendLine(Lang.Get("phototesting:wetplate-dry"));
            }
        }

        // Stores remaining wet time and clears live timer attributes while plate is in storage.
        public static void PauseWetTimerForStorage(IWorldAccessor world, ItemStack stack)
        {
            if (stack?.Attributes == null) return;

            double duration = stack.Attributes.GetDouble(WetDurationHours, 0);
            if (duration <= 0)
            {
                stack.Attributes.RemoveAttribute(StoredRemainingWetHours);
                return;
            }

            double remaining = GetRemainingWetHours(world, stack);
            if (remaining < 0) remaining = 0;

            stack.Attributes.SetDouble(StoredRemainingWetHours, remaining);
            stack.Attributes.RemoveAttribute(WetCreatedTotalHours);
            stack.Attributes.RemoveAttribute(WetDurationHours);
        }

        // Restores a paused wet timer back into live countdown attributes.
        public static void ResumeWetTimerFromStorage(IWorldAccessor world, ItemStack stack)
        {
            if (stack?.Attributes == null) return;

            double remaining = stack.Attributes.GetDouble(StoredRemainingWetHours, -1);
            if (remaining < 0) return;

            stack.Attributes.RemoveAttribute(StoredRemainingWetHours);

            if (remaining <= 0)
            {
                stack.Attributes.RemoveAttribute(WetCreatedTotalHours);
                stack.Attributes.RemoveAttribute(WetDurationHours);
                return;
            }

            if (world?.Calendar == null)
            {
                stack.Attributes.SetDouble(WetDurationHours, remaining);
                stack.Attributes.RemoveAttribute(WetCreatedTotalHours);
                return;
            }

            stack.Attributes.SetDouble(WetCreatedTotalHours, world.Calendar.TotalHours);
            stack.Attributes.SetDouble(WetDurationHours, remaining);
        }
    }

}
