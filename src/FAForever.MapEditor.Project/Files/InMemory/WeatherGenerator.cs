using System.Numerics;

namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// A place where clouds form. A setting it leaves out is left to the game.
    /// </summary>
    public sealed record WeatherGenerator
    {
        public required string Name { get; init; }

        public Vector3 Position { get; init; }

        /// <summary>
        /// The rotation around each axis, in radians.
        /// </summary>
        public Vector3 Orientation { get; init; }

        /// <summary>
        /// The cloud type to form, or <c>None</c> for the weather definition's.
        /// </summary>
        public string? ForceType { get; init; }

        public double? SpawnChance { get; init; }

        public double? CloudCount { get; init; }

        public double? CloudCountRange { get; init; }

        public double? CloudEmitterScale { get; init; }

        public double? CloudEmitterScaleRange { get; init; }

        public double? CloudHeight { get; init; }

        public double? CloudHeightRange { get; init; }

        public double? CloudSpread { get; init; }
    }
}
