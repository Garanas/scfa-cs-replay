namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// The weather of the map: its style and the cloud types that may appear.
    /// </summary>
    public sealed record WeatherDefinitionDto
    {
        public required string Name { get; init; }

        public required Vector3Dto Position { get; init; }

        /// <summary>
        /// The rotation around each axis, in radians.
        /// </summary>
        public Vector3Dto? Orientation { get; init; }

        /// <summary>
        /// E.g. <c>Desert</c>, <c>Evergreen</c>, <c>Tundra</c>.
        /// </summary>
        public required string MapStyle { get; init; }

        public string? Description { get; init; }

        /// <summary>
        /// The direction the clouds drift in.
        /// </summary>
        public required Vector3Dto DriftDirection { get; init; }

        /// <summary>
        /// The cloud types and how likely each is.
        /// </summary>
        public required IReadOnlyList<WeatherChanceDto> Types { get; init; }
    }
}
