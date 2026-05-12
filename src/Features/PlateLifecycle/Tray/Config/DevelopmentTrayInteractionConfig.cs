using Phototesting.AdminTooling;

namespace Phototesting.PlateLifecycle.Tray.Config
{
    public sealed class DevelopmentTrayInteractionConfig
    {
        public TimedInteractionConfig Developer = new TimedInteractionConfig
        {
            DurationSeconds = 1.25f
        };

        public TimedInteractionConfig Fixer = new TimedInteractionConfig
        {
            DurationSeconds = 1.25f
        };

        internal void ClampInPlace()
        {
            Developer ??= new TimedInteractionConfig { DurationSeconds = 1.25f };
            Fixer ??= new TimedInteractionConfig { DurationSeconds = 1.25f };
            Developer.ClampInPlace();
            Fixer.ClampInPlace();
        }
    }
}
