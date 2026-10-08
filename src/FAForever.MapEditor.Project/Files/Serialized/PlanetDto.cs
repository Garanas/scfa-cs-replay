namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// A planet of the sky dome.
    /// </summary>
    public sealed record PlanetDto
    {
        public required Vector3Dto Position { get; init; }

        public required float Rotation { get; init; }

        public required Vector2Dto Scale { get; init; }

        public required Vector4Dto Uv { get; init; }
    }
}
