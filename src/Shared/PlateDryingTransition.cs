using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Phototesting.AdminTooling;

namespace Phototesting
{
    // Wraps the vanilla EnumTransitionType.Dry pipeline so plate code can ignore
    // the underlying transitionstate tree and just ask "is this plate dry yet?".
    public static class PlateDryingTransition
    {
        // Marker set by OnTransitionNow once the Dry transition completes.
        public const string AttrDried = "phototestingPlateDried";

        // Marker for processes whose plates never dry (e.g. bromide).
        public const string AttrNeverDries = "phototestingPlateNeverDries";

        // Marker set by PlateBox while the stack is in storage; ItemPlateBase reads
        // this in GetTransitionRateMul to slow/freeze drying based on config.
        public const string AttrStoredInPlateBox = "phototestingPlateBoxStored";

        // Resolves how fast (0 = pause, 1 = full rate) plates dry while inside a plate box.
        public static float ResolveStorageDryingRateMul(ICoreAPI? api)
        {
            return PhotoTestingConfigAccess.ResolveConfig(api)?.PlateProcessing?.PlateBoxDryingMultiplier
                ?? 0f;
        }

        // Resolves the configured wet plate lifetime in hours with a safe fallback.
        public static double ResolveWetDurationHours(ICoreAPI? api)
        {
            return PhotoTestingConfigAccess.ResolveConfig(api)?.PlateProcessing?.WetPlateDurationHours
                ?? 0.66;
        }

        // True once the underlying Dry transition has finished and the plate is sealed dry.
        public static bool IsDry(IWorldAccessor? world, ItemStack? stack)
        {
            if (stack?.Attributes == null) return false;
            if (stack.Attributes.GetBool(AttrDried)) return true;
            if (stack.Attributes.GetBool(AttrNeverDries)) return false;
            if (world == null || stack.Collectible == null) return false;

            TransitionState? state = stack.Collectible.UpdateAndGetTransitionState(
                world, new DummySlot(stack), EnumTransitionType.Dry);
            return state != null && state.TransitionLevel >= 1f;
        }

        // Hours of remaining "fresh" wet time, or 0 once drying has begun/finished.
        public static double GetRemainingHours(IWorldAccessor? world, ItemStack? stack)
        {
            if (stack?.Attributes == null) return 0;
            if (stack.Attributes.GetBool(AttrDried)) return 0;
            if (stack.Attributes.GetBool(AttrNeverDries)) return double.PositiveInfinity;
            if (world == null || stack.Collectible == null) return 0;

            TransitionState? state = stack.Collectible.UpdateAndGetTransitionState(
                world, new DummySlot(stack), EnumTransitionType.Dry);
            return state != null ? state.FreshHoursLeft : 0;
        }

        // Appends "wet for N more minutes" / "dry" tooltip text.
        public static void AppendInfo(IWorldAccessor? world, ItemStack? stack, StringBuilder dsc)
        {
            if (stack == null || dsc == null) return;

            if (stack.Attributes?.GetBool(AttrNeverDries) == true)
            {
                dsc.AppendLine(Lang.Get("phototesting:wetplate-wetness-permanent"));
                return;
            }

            double hoursLeft = GetRemainingHours(world, stack);
            if (hoursLeft > 0 && !double.IsPositiveInfinity(hoursLeft))
            {
                int minutesLeft = (int)Math.Ceiling(hoursLeft * 60);
                dsc.AppendLine(string.Format(Lang.Get("phototesting:wetplate-wetness"), minutesLeft));
            }
            else
            {
                dsc.AppendLine(Lang.Get("phototesting:wetplate-dry"));
            }
        }

        // Resets the dry transition to "freshly wet". A non-positive freshHours marks the
        // plate as never-drying (e.g. for the bromide process).
        public static void ResetTimer(IWorldAccessor world, ItemStack stack, double freshHoursOverride)
        {
            if (world?.Calendar == null || stack?.Attributes == null) return;

            stack.Attributes.RemoveAttribute("transitionstate");
            stack.Attributes.RemoveAttribute(AttrDried);

            if (freshHoursOverride <= 0)
            {
                stack.Attributes.SetBool(AttrNeverDries, true);
                return;
            }

            stack.Attributes.RemoveAttribute(AttrNeverDries);

            TreeAttribute attr = new();
            attr.SetDouble("createdTotalHours", world.Calendar.TotalHours);
            attr.SetDouble("lastUpdatedTotalHours", world.Calendar.TotalHours);
            attr["freshHours"] = new FloatArrayAttribute(new[] { (float)freshHoursOverride });
            attr["transitionHours"] = new FloatArrayAttribute(new[] { 0.001f });
            attr["transitionedHours"] = new FloatArrayAttribute(new[] { 0f });
            stack.Attributes["transitionstate"] = attr;
        }

        // Removes any drying state from a stack (used when a plate transitions to a
        // lifecycle stage that has no wet timer, e.g. Finished).
        public static void Clear(ItemStack? stack)
        {
            if (stack?.Attributes == null) return;
            stack.Attributes.RemoveAttribute("transitionstate");
            stack.Attributes.RemoveAttribute(AttrDried);
            stack.Attributes.RemoveAttribute(AttrNeverDries);
        }

        // Marks/unmarks a stack as currently held inside a plate box. ItemPlateBase
        // reads this in GetTransitionRateMul to apply the storage multiplier.
        public static void SetStoredInPlateBox(ItemStack? stack, bool stored)
        {
            if (stack?.Attributes == null) return;
            if (stored) stack.Attributes.SetBool(AttrStoredInPlateBox, true);
            else stack.Attributes.RemoveAttribute(AttrStoredInPlateBox);
        }

        // Manually rolls forward the transition state (used by PlateBox before/after
        // storage so the storage multiplier is correctly applied).
        public static void TickNow(IWorldAccessor? world, ItemStack? stack)
        {
            if (world == null || stack?.Collectible == null) return;
            stack.Collectible.UpdateAndGetTransitionState(
                world, new DummySlot(stack), EnumTransitionType.Dry);
        }
    }
}
