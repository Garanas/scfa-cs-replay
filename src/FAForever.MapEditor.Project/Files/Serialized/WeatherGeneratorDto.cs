namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// A place where clouds form. A setting it leaves out is left to the game.
    /// </summary>
    public sealed record WeatherGeneratorDto
    {
        public required string Name { get; init; }

        public required Vector3Dto Position { get; init; }

        /// <summary>
        /// The rotation around each axis, in radians.
        /// </summary>
        public Vector3Dto? Orientation { get; init; }

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
