using System.Numerics;

namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// A mass or hydrocarbon deposit.
    /// </summary>
    public sealed record ResourceMarker
    {
        public required string Name { get; init; }

        public ResourceKind Kind { get; init; }

        public Vector3 Position { get; init; }

        /// <summary>
        /// The rotation around each axis, in radians.
        /// </summary>
        public Vector3 Orientation { get; init; }

        /// <summary>
        /// The army without which an adaptive map leaves the deposit out.
        /// </summary>
        public string? SpawnWithArmy { get; init; }
    }
}
