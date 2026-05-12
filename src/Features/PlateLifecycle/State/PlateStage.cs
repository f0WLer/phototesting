namespace Phototesting.PlateLifecycle
{
    /// <summary>Ordered lifecycle stages for a photographic plate.</summary>
    public enum PlateStage
    {
        Unknown = 0,
        /// <summary>Unpolished raw glass — starting state.</summary>
        Rough,
        /// <summary>Polished and cleaned — ready to coat.</summary>
        Clean,
        /// <summary>In-progress sensitization — at least one step complete, not yet fully sensitized.</summary>
        Sensitizing,
        /// <summary>Coated and silver-sensitized — ready to expose.</summary>
        Sensitized,
        /// <summary>Exposed in camera — latent image present.</summary>
        Exposed,
        /// <summary>Partially developed — not yet enough pours.</summary>
        Developing,
        /// <summary>Fully developed — ready for fixer.</summary>
        Developed,
        /// <summary>Fixed and finished — permanent image.</summary>
        Finished
    }

    internal static class PlateStageUtil
    {
        internal static string ToAttributeString(PlateStage stage) => stage switch
        {
            PlateStage.Rough       => "rough",
            PlateStage.Clean       => "clean",
            PlateStage.Sensitizing => "sensitizing",
            PlateStage.Sensitized  => "sensitized",
            PlateStage.Exposed     => "exposed",
            PlateStage.Developing  => "developing",
            PlateStage.Developed   => "developed",
            PlateStage.Finished    => "finished",
            _                      => string.Empty
        };

        internal static PlateStage FromAttributeString(string? value) => value switch
        {
            "rough"      => PlateStage.Rough,
            "clean"      => PlateStage.Clean,
            "sensitizing"=> PlateStage.Sensitizing,
            "sensitized" => PlateStage.Sensitized,
            "exposed"    => PlateStage.Exposed,
            "developing" => PlateStage.Developing,
            "developed"  => PlateStage.Developed,
            "finished"   => PlateStage.Finished,
            _            => PlateStage.Unknown
        };
    }
}

