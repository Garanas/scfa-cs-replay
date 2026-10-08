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
        /// The names of the markers, in order.
        /// </summary>
        public ImmutableArray<string> Markers { get; init; } = [];
    }
}
