namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// Four numbers, e.g. a colour with alpha.
    /// </summary>
    public sealed record Vector4Dto
    {
        public required float X { get; init; }

        public required float Y { get; init; }

        public required float Z { get; init; }

        public required float W { get; init; }
    }
}
