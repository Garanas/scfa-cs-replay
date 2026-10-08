namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// <c>decals.json</c>: the decals of the terrain and their groups.
    /// </summary>
    public sealed record DecalsDto
    {
        public required IReadOnlyList<DecalDto> Decals { get; init; }

        public required IReadOnlyList<DecalGroupDto> Groups { get; init; }
    }
}
