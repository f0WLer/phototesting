using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Phototesting.PlateLifecycle
{
    internal static class TrayTimedInteractionState
    {
        internal const string TimedAttrKey = "phototestingDevTrayTimed";
        internal const string TimedNeedReleaseKey = "phototestingDevTrayNeedRelease";
        internal const string TimedActionKey = "action";
        internal const string TimedXKey = "x";
        internal const string TimedYKey = "y";
        internal const string TimedZKey = "z";
        internal const string TimedStartMsKey = "startMs";
        internal const string TimedDurationMsKey = "durationMs";

        // Starts or replaces the player's current timed tray interaction for a specific tray position and action key.
        internal static void Begin(IPlayer? byPlayer, BlockPos? pos, string action, float durationSeconds)
        {
            if (byPlayer?.Entity?.Attributes == null || pos == null) return;

            ITreeAttribute tree = byPlayer.Entity.Attributes.GetOrAddTreeAttribute(TimedAttrKey);
            tree.SetString(TimedActionKey, action);
            tree.SetInt(TimedXKey, pos.X);
            tree.SetInt(TimedYKey, pos.Y);
            tree.SetInt(TimedZKey, pos.Z);

            long nowMs = byPlayer.Entity?.World?.ElapsedMilliseconds ?? 0;

            if (nowMs <= 0) nowMs = Environment.TickCount64;
            tree.SetLong(TimedStartMsKey, nowMs);

            if (durationSeconds > 0f)
            {
                int durationMs = (int)Math.Round(durationSeconds * 1000f);
                if (durationMs < 1) durationMs = 1;
                tree.SetInt(TimedDurationMsKey, durationMs);
            }
        }

        // Checks whether the player is still timing the requested action at the requested tray position.
        internal static bool IsActive(IPlayer? byPlayer, BlockPos? pos, string action)
        {
            if (byPlayer?.Entity?.Attributes == null || pos == null) return false;

            ITreeAttribute? tree = byPlayer.Entity.Attributes.GetTreeAttribute(TimedAttrKey);
            if (tree == null) return false;
            if (!tree.GetString(TimedActionKey, string.Empty).Equals(action, StringComparison.Ordinal)) return false;

            return tree.GetInt(TimedXKey) == pos.X && tree.GetInt(TimedYKey) == pos.Y && tree.GetInt(TimedZKey) == pos.Z;
        }

        // Clears the player's active timed tray interaction state.
        internal static void Clear(IPlayer? byPlayer)
        {
            if (byPlayer?.Entity?.Attributes == null) return;
            byPlayer.Entity.Attributes.RemoveAttribute(TimedAttrKey);
        }

        // Returns whether the client is waiting for RMB release before it may begin another timed tray action.
        internal static bool NeedsRelease(IPlayer? byPlayer)
        {
            try
            {
                return byPlayer?.Entity?.Attributes?.GetInt(TimedNeedReleaseKey, 0) != 0;
            }
            catch
            {
                return false;
            }
        }

        // Sets the RMB-release latch after a timed tray action completes.
        internal static void SetNeedsRelease(IPlayer? byPlayer)
        {
            try
            {
                byPlayer?.Entity?.Attributes?.SetInt(TimedNeedReleaseKey, 1);
            }
            catch (Exception ex)
            {
                Log.Debug(byPlayer?.Entity?.Api?.Logger, "SetNeedsRelease: entity attribute write failed: {0}", ex.Message);
            }
        }

        // Clears the RMB-release latch once the client sees that RMB is actually up again.
        internal static void ClearNeedsRelease(IPlayer? byPlayer)
        {
            try
            {
                byPlayer?.Entity?.Attributes?.RemoveAttribute(TimedNeedReleaseKey);
            }
            catch
            {
                // ignore
            }
        }
    }
}

