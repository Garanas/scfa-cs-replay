namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// Two numbers, e.g. a direction on the water surface.
    /// </summary>
    public sealed record Vector2Dto
    {
        public required float X { get; init; }

        public required float Y { get; init; }
    }
}
