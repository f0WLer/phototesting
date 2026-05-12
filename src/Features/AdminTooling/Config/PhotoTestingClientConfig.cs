namespace Phototesting.AdminTooling
{
    // Client-only quality-of-life and debug toggles persisted in mod config.
    // Includes bounds checks so chat/tooling values stay within sane limits.
    public sealed class PhotoTestingClientConfig
    {
        /// <summary>Tooltip caption truncation length. Set 0 to disable tooltip truncation.</summary>
        public int CaptionTooltipMaxLength = 180;

        /// <summary>How often client sends photo-seen ping updates. 0 disables pings.</summary>
        public int PhotoSeenPingIntervalSeconds = 300;

        /// <summary>If true, shows the zoom mechanism source/status in chat/log.</summary>
        public bool ShowZoomMechanismChat = false;

        /// <summary>If true, enables verbose debug/dev log messages.</summary>
        public bool ShowDebugLogs = false;

        // Clamps client-only config values so chat/JSON edits cannot push invalid ranges.
        internal void ClampInPlace()
        {
            // Keep within a reasonable range; 0 or below disables truncation.
            if (CaptionTooltipMaxLength < 0) CaptionTooltipMaxLength = 0;
            if (CaptionTooltipMaxLength > 5000) CaptionTooltipMaxLength = 5000;

            if (PhotoSeenPingIntervalSeconds < 0) PhotoSeenPingIntervalSeconds = 0;
            if (PhotoSeenPingIntervalSeconds > 24 * 60 * 60) PhotoSeenPingIntervalSeconds = 24 * 60 * 60;
        }
    }
}
