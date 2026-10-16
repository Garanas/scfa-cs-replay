namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// A layer of cirrus clouds.
    /// </summary>
    public sealed record CirrusLayerDto
    {
        public required Vector2Dto Frequency { get; init; }

        public required float Speed { get; init; }

        public required Vector2Dto Direction { get; init; }
    }
}
