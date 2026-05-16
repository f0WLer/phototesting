namespace Phototesting.PlateLifecycle
{
    public sealed class PlateProcessingConfig
    {
        /// <summary>Developer/fixer units consumed per tray pour. Lower = cheaper processing, higher = costlier.</summary>
        public int DevelopmentTrayChemicalUnitsPerUse = 40;

        /// <summary>Hold duration to polish rough plates. 0 = instant polish.</summary>
        public float PolishSeconds = 2f;

        /// <summary>Hold duration for one clean-plate sensitization pour interaction. 0 = instant.</summary>
        public float SensitizationPourSeconds = 1.5f;

        /// <summary>Hold duration for a dry/air-dry sensitization step. Steps can override this individually. 0 = instant.</summary>
        public float SensitizationDrySeconds = 15f;

        /// <summary>If true, polishing consumes plain cloth per action.</summary>
        public bool ConsumePlainClothOnPolish = false;

        /// <summary>Plain cloth consumed per polish when ConsumePlainClothOnPolish is true.</summary>
        public int PlainClothConsumedPerPolish = 1;

        /// <summary>How long a freshly-sensitized plate stays wet, in in-game hours. This is affected by the world's time speed. Default 0.66 (40 minutes). Server-authoritative.</summary>
        public double WetPlateDurationHours = 0.66;

        /// <summary>How fast plates dry while inside a plate box. 0 = paused (default), 1 = full open-air rate.</summary>
        public float PlateBoxDryingMultiplier = 0f;

        internal void ClampInPlace()
        {
            if (DevelopmentTrayChemicalUnitsPerUse < 1) DevelopmentTrayChemicalUnitsPerUse = 1;
            if (DevelopmentTrayChemicalUnitsPerUse > 5000) DevelopmentTrayChemicalUnitsPerUse = 5000;

            if (PolishSeconds < 0f) PolishSeconds = 0f;
            if (PolishSeconds > 30f) PolishSeconds = 30f;

            if (SensitizationPourSeconds < 0f) SensitizationPourSeconds = 0f;
            if (SensitizationPourSeconds > 30f) SensitizationPourSeconds = 30f;

            if (SensitizationDrySeconds < 0f) SensitizationDrySeconds = 0f;
            if (SensitizationDrySeconds > 300f) SensitizationDrySeconds = 300f;

            if (PlainClothConsumedPerPolish < 0) PlainClothConsumedPerPolish = 0;
            if (PlainClothConsumedPerPolish > 64) PlainClothConsumedPerPolish = 64;

            if (WetPlateDurationHours < 0.01) WetPlateDurationHours = 0.01;
            if (WetPlateDurationHours > 720.0) WetPlateDurationHours = 720.0;

            if (PlateBoxDryingMultiplier < 0f) PlateBoxDryingMultiplier = 0f;
            if (PlateBoxDryingMultiplier > 1f) PlateBoxDryingMultiplier = 1f;
        }
    }
}
