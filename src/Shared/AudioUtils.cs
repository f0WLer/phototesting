using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Phototesting
{
    /// <summary>
    /// Shared non-blocking audio helpers for minor pitch variance and best-effort playback.
    /// </summary>
    public static class AudioUtils
    {
        // Adds slight pitch variance so repeated SFX do not sound mechanically identical.
        public static float NextRandomPitch(IWorldAccessor? world)
        {
            const float basePitch = 0.92f;
            const float spread = 0.16f;

            try
            {
                if (world?.Rand != null)
                {
                    return basePitch + (float)world.Rand.NextDouble() * spread;
                }
            }
            catch
            {
                // Fallback below.
            }

            return 1f;
        }

        // Plays an entity-attached sound without allowing audio failures to impact gameplay flow.
        public static void FireAndForgetEntitySound(IWorldAccessor? world, AssetLocation sound, Entity? entity, float pitch = 1f, float range = 16f)
        {
            if (world == null || entity == null) return;

            BestEffort.Try(world.Logger, "play entity sound", () => world.PlaySoundAt(sound, entity, null, true, range, pitch));
        }
    }
}
