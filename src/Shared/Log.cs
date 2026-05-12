using Vintagestory.API.Common;

namespace Phototesting
{
    /// <summary>
    /// Centralized log entry points that prepend the standard "[phototesting] " prefix.
    /// Use these instead of calling <see cref="ILogger"/> directly so the prefix lives in one place.
    /// </summary>
    internal static class Log
    {
        private const string Prefix = "[phototesting] ";

        internal static void Warn(ILogger? logger, string format, params object[] args)
            => logger?.Warning(Prefix + format, args);

        internal static void Error(ILogger? logger, string format, params object[] args)
            => logger?.Error(Prefix + format, args);

        internal static void Debug(ILogger? logger, string format, params object[] args)
            => logger?.Debug(Prefix + format, args);

        internal static void Notify(ILogger? logger, string format, params object[] args)
            => logger?.Notification(Prefix + format, args);
    }
}
