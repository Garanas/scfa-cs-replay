using System.Numerics;

namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// A start position, named after its army.
    /// </summary>
    public sealed record Spawn
    {
        /// <summary>
        /// E.g. <c>ARMY_1</c>.
        /// </summary>
        public required string Army { get; init; }

        public Vector3 Position { get; init; }

        /// <summary>
        /// The rotation around each axis, in radians.
        /// </summary>
        public Vector3 Orientation { get; init; }
    }
}
