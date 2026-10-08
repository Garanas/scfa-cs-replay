using System.Collections.Immutable;

namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// The markers of the map, by what they are, with the areas and chains that name them.
    /// </summary>
    public sealed record MarkerSet
    {
        public ImmutableArray<Spawn> Spawns { get; init; } = [];

        public ImmutableArray<ResourceMarker> Resources { get; init; } = [];

        /// <summary>
        /// The nodes of the graphs the AI finds its way with.
        /// </summary>
        public ImmutableArray<PathNode> PathNodes { get; init; } = [];

        /// <summary>
        /// The markers the AI and scripts read, such as rally points, and the markers of a type the editor does not know.
        /// </summary>
        public ImmutableArray<AiMarker> AiMarkers { get; init; } = [];

        public ImmutableArray<WeatherDefinition> WeatherDefinitions { get; init; } = [];

        public ImmutableArray<WeatherGenerator> WeatherGenerators { get; init; } = [];

        public ImmutableArray<Area> Areas { get; init; } = [];

        public ImmutableArray<Chain> Chains { get; init; } = [];
    }
}
