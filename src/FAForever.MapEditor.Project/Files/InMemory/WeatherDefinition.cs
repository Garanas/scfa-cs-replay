using System.Collections.Immutable;
using System.Numerics;

namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// The weather of the map: its style and the cloud types that may appear.
    /// </summary>
    public sealed record WeatherDefinition
    {
        public required string Name { get; init; }

        public Vector3 Position { get; init; }

        /// <summary>
        /// The rotation around each axis, in radians.
        /// </summary>
        public Vector3 Orientation { get; init; }

        /// <summary>
        /// E.g. <c>Desert</c>, <c>Evergreen</c>, <c>Tundra</c>.
        /// </summary>
        public required string MapStyle { get; init; }

        public string? Description { get; init; }

        /// <summary>
        /// The direction the clouds drift in.
        /// </summary>
        public Vector3 DriftDirection { get; init; }

        /// <summary>
        /// The cloud types and how likely each is.
        /// </summary>
        public ImmutableArray<WeatherChance> Types { get; init; } = [];
    }
}
