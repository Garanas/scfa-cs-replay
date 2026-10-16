namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// A named group of decals.
    /// </summary>
    public sealed record DecalGroupDto
    {
        public required int Id { get; init; }

        public required string Name { get; init; }

        public required IReadOnlyList<int> DecalIds { get; init; }
    }
}
