namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// A cloud type and how likely it is.
    /// </summary>
    public sealed record WeatherChanceDto
    {
        /// <summary>
        /// E.g. <c>LightStratus</c>, or <c>None</c>.
        /// </summary>
        public required string Type { get; init; }

        public required double Chance { get; init; }
    }
}
