namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// A normal map layer of the water surface.
    /// </summary>
    public sealed record WaveTextureDto
    {
        public required string Texture { get; init; }

        public required float NormalRepeat { get; init; }

        public required Vector2Dto NormalMovement { get; init; }
    }
}
