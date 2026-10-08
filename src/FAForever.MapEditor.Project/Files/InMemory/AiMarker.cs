using System.Numerics;

namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// A marker the AI or a script reads, e.g. a rally point or an expansion area.
    /// </summary>
    public sealed record AiMarker
    {
        public required string Name { get; init; }

        /// <summary>
        /// The type as the game names it, e.g. <c>Rally Point</c> or <c>Expansion Area</c>.
        /// </summary>
        public required string Type { get; init; }

        public Vector3 Position { get; init; }

        /// <summary>
        /// The rotation around each axis, in radians.
        /// </summary>
        public Vector3 Orientation { get; init; }
    }
}
