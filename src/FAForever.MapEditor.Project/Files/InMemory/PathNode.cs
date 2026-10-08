using System.Collections.Immutable;
using System.Numerics;

namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// A node of the graph the AI finds its way with on one layer.
    /// </summary>
    public sealed record PathNode
    {
        public required string Name { get; init; }

        public PathLayer Layer { get; init; }

        public Vector3 Position { get; init; }

        /// <summary>
        /// The rotation around each axis, in radians.
        /// </summary>
        public Vector3 Orientation { get; init; }

        /// <summary>
        /// The names of the nodes this one links to.
        /// </summary>
        public ImmutableArray<string> AdjacentTo { get; init; } = [];
    }
}
