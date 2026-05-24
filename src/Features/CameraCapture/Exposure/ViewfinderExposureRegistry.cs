using Phototesting.CameraCapture.Exposure;

namespace Phototesting.CameraCapture
{
    /// <summary>
    /// In-memory registry that maps stable exposure IDs to active <see cref="IGameplayExposureAccumulator"/> instances.
    /// Entries survive viewfinder exit so a paused exposure can be resumed when the player
    /// looks through the camera again. An entry is evicted when the plate seals (transitions to the Exposed stage).
    /// Buffers are session-scoped and lost on relog, but the accumulated frame count persists in plate item attributes.
    /// </summary>
    internal static class ViewfinderExposureRegistry
    {
        private static readonly Dictionary<string, IGameplayExposureAccumulator> _registry
            = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Registers <paramref name="accumulator"/> under <paramref name="exposureId"/>, disposing any previously registered instance for the same key.</summary>
        internal static void Register(string exposureId, IGameplayExposureAccumulator accumulator)
        {
            if (string.IsNullOrEmpty(exposureId)) return;
            if (_registry.TryGetValue(exposureId, out var old) && !ReferenceEquals(old, accumulator))
                old.Dispose();
            _registry[exposureId] = accumulator;
        }

        /// <summary>Retrieves the accumulator registered under <paramref name="exposureId"/>. Returns <see langword="false"/> when the ID is absent.</summary>
        internal static bool TryGet(string exposureId, out IGameplayExposureAccumulator? accumulator)
        {
            if (string.IsNullOrEmpty(exposureId)) { accumulator = null; return false; }
            return _registry.TryGetValue(exposureId, out accumulator);
        }

        /// <summary>Disposes and removes the accumulator registered under <paramref name="exposureId"/>. No-op when the ID is absent.</summary>
        internal static void Remove(string exposureId)
        {
            if (string.IsNullOrEmpty(exposureId)) return;
            if (_registry.TryGetValue(exposureId, out var acc))
            {
                acc.Dispose();
                _registry.Remove(exposureId);
            }
        }

        /// <summary>Disposes all registered accumulators and clears the registry.</summary>
        internal static void Clear()
        {
            foreach (var acc in _registry.Values) acc.Dispose();
            _registry.Clear();
        }
    }
}
