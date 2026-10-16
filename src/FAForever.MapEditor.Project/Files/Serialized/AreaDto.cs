namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// A named rectangle in game units, e.g. the playable area.
    /// </summary>
    public sealed record AreaDto
    {
        public required string Name { get; init; }

        public required double X0 { get; init; }

        public required double Z0 { get; init; }

        public required double X1 { get; init; }

        public required double Z1 { get; init; }
    }
}
