namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// The markers of the map, by what they are, with the areas and chains that name them.
    /// </summary>
    public sealed record MarkerSetDto
    {
        public required IReadOnlyList<SpawnDto> Spawns { get; init; }

        public required IReadOnlyList<ResourceMarkerDto> Resources { get; init; }

        /// <summary>
        /// The nodes of the graphs the AI finds its way with.
        /// </summary>
        public required IReadOnlyList<PathNodeDto> PathNodes { get; init; }

        /// <summary>
        /// The markers the AI and scripts read, such as rally points, and the markers of a type the editor does not know.
        /// </summary>
        public required IReadOnlyList<AiMarkerDto> AiMarkers { get; init; }

        public required IReadOnlyList<WeatherDefinitionDto> WeatherDefinitions { get; init; }

        public required IReadOnlyList<WeatherGeneratorDto> WeatherGenerators { get; init; }

        public required IReadOnlyList<AreaDto> Areas { get; init; }

        public required IReadOnlyList<ChainDto> Chains { get; init; }
    }
}
