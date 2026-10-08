namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// How far the no-rush circle of an army lies from its start position; a missing offset is none.
    /// </summary>
    public sealed record NoRushOffset
    {
        public double? X { get; init; }

        public double? Y { get; init; }
    }
}
