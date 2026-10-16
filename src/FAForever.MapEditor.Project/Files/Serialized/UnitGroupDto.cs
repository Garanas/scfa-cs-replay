namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// A named group of units and further groups.
    /// </summary>
    public sealed record UnitGroupDto
    {
        public required string Name { get; init; }

        public required IReadOnlyList<UnitGroupDto> Groups { get; init; }

        public required IReadOnlyList<UnitDto> Units { get; init; }

        /// <summary>
        /// The unit preset the group was placed from (user story U-9); absent for a group made
        /// otherwise, and in every project saved before instances.
        /// </summary>
        public PresetInstanceDto? Instance { get; init; }
    }

    /// <summary>
    /// How a group came from a unit preset: its name, a fingerprint of its content when placed,
    /// the place (ogrids) and heading (radians) it was placed with, and when.
    /// </summary>
    public sealed record PresetInstanceDto
    {
        public required string Preset { get; init; }

        public required string Fingerprint { get; init; }

        public required float X { get; init; }

        public required float Z { get; init; }

        public required float Heading { get; init; }

        public required DateTimeOffset PlacedAt { get; init; }
    }
}
