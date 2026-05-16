namespace Phototesting.PlateLifecycle
{
    public sealed class TimedInteractionConfig
    {
        // Keep generic so we can add start/end hooks (sounds/particles) later.
        public float DurationSeconds = 1.25f;

        internal void ClampInPlace()
        {
            if (DurationSeconds < 0.05f) DurationSeconds = 0.05f;
            if (DurationSeconds > 30f) DurationSeconds = 30f;
        }
    }
}
