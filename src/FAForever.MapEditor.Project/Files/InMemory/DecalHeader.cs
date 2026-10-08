namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// Two numbers before the decals whose meaning is unknown. In most maps the first exceeds every decal id and the second every group id.
    /// </summary>
    public sealed record DecalHeader
    {
        public int First { get; init; }

        public int Second { get; init; }
    }
}
