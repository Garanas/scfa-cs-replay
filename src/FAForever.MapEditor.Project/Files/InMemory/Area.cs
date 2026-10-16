namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// A named rectangle in game units, e.g. the playable area.
    /// </summary>
    public sealed record Area
    {
        public required string Name { get; init; }

        public double X0 { get; init; }

        public double Z0 { get; init; }

        public double X1 { get; init; }

        public double Z1 { get; init; }
    }
}
