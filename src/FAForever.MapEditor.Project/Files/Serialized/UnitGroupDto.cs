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
    }
}
