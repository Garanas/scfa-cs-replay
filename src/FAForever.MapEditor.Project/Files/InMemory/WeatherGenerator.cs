namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// A place where clouds form. A setting it leaves out is left to the game.
    /// </summary>
    public sealed record WeatherGenerator : Marker
    {
        /// <summary>
        /// The cloud type to form, or <c>None</c> for the weather definition's.
        /// </summary>
        public string? ForceType { get; init; }

        public double? SpawnChance { get; init; }

        public double? CloudCount { get; init; }

        public double? CloudCountRange { get; init; }

        public double? CloudEmitterScale { get; init; }

        public double? CloudEmitterScaleRange { get; init; }

        public double? CloudHeight { get; init; }

        public double? CloudHeightRange { get; init; }

        public double? CloudSpread { get; init; }

        public override Color32 Color => Color32.FromArgb(0xff808000);
    }
}
