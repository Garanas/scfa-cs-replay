namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// Three numbers: a position, a direction or a colour (red, green, blue).
    /// </summary>
    public sealed record Vector3Dto
    {
        public required float X { get; init; }

        public required float Y { get; init; }

        public required float Z { get; init; }
    }
}
