using System.Numerics;

namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// A unit placed on the map, e.g. a civilian building or a wreck.
    /// </summary>
    public sealed record Unit
    {
        /// <summary>
        /// E.g. <c>UNIT_5</c>.
        /// </summary>
        public required string Name { get; init; }

        /// <summary>
        /// The unit's blueprint id, e.g. <c>uel0307</c>.
        /// </summary>
        public required string Blueprint { get; init; }

        public Vector3 Position { get; init; }

        /// <summary>
        /// The rotation around each axis, in radians.
        /// </summary>
        public Vector3 Orientation { get; init; }
    }
}
