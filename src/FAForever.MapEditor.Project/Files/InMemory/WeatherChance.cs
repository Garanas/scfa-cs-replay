namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// A cloud type and how likely it is.
    /// </summary>
    public sealed record WeatherChance
    {
        /// <summary>
        /// E.g. <c>LightStratus</c>, or <c>None</c>.
        /// </summary>
        public required string Type { get; init; }

        public double Chance { get; init; }
    }
}
