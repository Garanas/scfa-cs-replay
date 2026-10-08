namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// How far the no-rush circle of an army lies from its start position; a missing offset is none.
    /// </summary>
    public sealed record NoRushOffsetDto
    {
        public double? X { get; init; }

        public double? Y { get; init; }
    }
}
