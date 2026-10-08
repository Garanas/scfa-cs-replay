namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// Two numbers before the decals whose meaning is unknown. In most maps the first exceeds every decal id and the second every group id.
    /// </summary>
    public sealed record DecalHeaderDto
    {
        public required int First { get; init; }

        public required int Second { get; init; }
    }
}
