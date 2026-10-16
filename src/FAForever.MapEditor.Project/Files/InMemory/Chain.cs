using System.Collections.Immutable;

namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// A named list of markers, e.g. a patrol route.
    /// </summary>
    public sealed record Chain
    {
        public required string Name { get; init; }

        /// <summary>
        /// The ids of the markers, in order (<see cref="Marker.Id"/>).
        /// </summary>
        public ImmutableArray<int> MarkerIds { get; init; } = [];
    }
}
