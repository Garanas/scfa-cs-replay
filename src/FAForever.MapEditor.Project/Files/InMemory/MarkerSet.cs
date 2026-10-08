using System.Collections.Immutable;

namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// The markers of the map, by what they are, with the areas and chains. Every marker has an id
    /// that no other marker of the set has (<see cref="Marker.Id"/>).
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

        /// <summary>
        /// Every marker, of every kind.
        /// </summary>
        public IEnumerable<Marker> All =>
            Spawns.Cast<Marker>().Concat(Resources).Concat(PathNodes).Concat(AiMarkers).Concat(WeatherDefinitions).Concat(WeatherGenerators);

        /// <summary>
        /// An id for a new marker: one more than the highest in use.
        /// </summary>
        public int NextId() => All.Select(marker => marker.Id).DefaultIfEmpty(0).Max() + 1;
    }
}
