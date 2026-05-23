using Phototesting.CameraCapture.Exposure;

namespace Phototesting.CameraCapture
{
    // In-memory registry mapping stable exposure IDs to active accumulator instances.
    // Entries survive viewfinder exit so a paused exposure can be resumed when the player
    // looks through the camera again. Entries are evicted when the plate seals (Exposed stage).
    // The registry is intentionally process-scoped (session-lifetime); buffers are lost on
    // session end but the accumulated frame count persists on the plate's item attributes.
    internal static class ViewfinderExposureRegistry
    {
        private static readonly Dictionary<string, IGameplayExposureAccumulator> _registry
            = new(StringComparer.OrdinalIgnoreCase);

        internal static void Register(string exposureId, IGameplayExposureAccumulator accumulator)
        {
            if (string.IsNullOrEmpty(exposureId)) return;
            if (_registry.TryGetValue(exposureId, out var old) && !ReferenceEquals(old, accumulator))
                old.Dispose();
            _registry[exposureId] = accumulator;
        }

        internal static bool TryGet(string exposureId, out IGameplayExposureAccumulator? accumulator)
        {
            if (string.IsNullOrEmpty(exposureId)) { accumulator = null; return false; }
            return _registry.TryGetValue(exposureId, out accumulator);
        }

        internal static void Remove(string exposureId)
        {
            if (string.IsNullOrEmpty(exposureId)) return;
            if (_registry.TryGetValue(exposureId, out var acc))
            {
                acc.Dispose();
                _registry.Remove(exposureId);
            }
        }

        internal static void Clear()
        {
            foreach (var acc in _registry.Values) acc.Dispose();
            _registry.Clear();
        }
    }
}
