using Vintagestory.API.Common;

namespace Phototesting
{
    internal readonly struct BestEffortLogPolicy
    {
        private BestEffortLogPolicy(bool warnOnFailure, int warningIntervalMs)
        {
            WarnOnFailure = warnOnFailure;
            WarningIntervalMs = warningIntervalMs < 1000 ? 1000 : warningIntervalMs;
        }

        internal bool WarnOnFailure { get; }
        internal int WarningIntervalMs { get; }

        internal static BestEffortLogPolicy DebugOnly => default;

        internal static BestEffortLogPolicy WarnRateLimited(int warningIntervalMs = 30000)
        {
            return new BestEffortLogPolicy(warnOnFailure: true, warningIntervalMs);
        }
    }

    internal static class BestEffort
    {
        private static readonly Dictionary<string, long> LastWarningMsByOperation = new(StringComparer.Ordinal);
        private static readonly object WarningGate = new();

        /// <summary>
        /// Executes a best-effort action, logging failures at debug level without interrupting gameplay.
        /// </summary>
        internal static void Try(ILogger? logger, string operation, Action action, BestEffortLogPolicy policy = default)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                LogFailure(logger, operation, ex, policy);
            }
        }

        /// <summary>
        /// Executes a best-effort function, returning a fallback value on failure.
        /// </summary>
        internal static T Try<T>(ILogger? logger, string operation, Func<T> func, T fallback = default!, BestEffortLogPolicy policy = default)
        {
            try
            {
                return func();
            }
            catch (Exception ex)
            {
                LogFailure(logger, operation, ex, policy);
                return fallback;
            }
        }

        private static void LogFailure(ILogger? logger, string operation, Exception ex, BestEffortLogPolicy policy)
        {
            if (logger == null) return;

            if (!policy.WarnOnFailure)
            {
                Log.Debug(logger, "best-effort '{0}' failed: {1}", operation, ex.Message);
                return;
            }

            long nowMs = Environment.TickCount64;
            bool shouldWarn;
            lock (WarningGate)
            {
                if (!LastWarningMsByOperation.TryGetValue(operation, out long lastWarnMs) || nowMs - lastWarnMs >= policy.WarningIntervalMs)
                {
                    LastWarningMsByOperation[operation] = nowMs;
                    shouldWarn = true;
                }
                else
                {
                    shouldWarn = false;
                }
            }

            if (shouldWarn)
            {
                Log.Warn(logger, "best-effort '{0}' failed: {1}", operation, ex.Message);
                return;
            }

            Log.Debug(logger, "best-effort '{0}' failed (suppressed warn): {1}", operation, ex.Message);
        }
    }
}
