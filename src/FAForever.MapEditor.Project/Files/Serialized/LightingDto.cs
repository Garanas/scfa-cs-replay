namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// How the sun lights the terrain and the units.
    /// </summary>
    public sealed record LightingDto
    {
        public required float Multiplier { get; init; }

        public required Vector3Dto SunDirection { get; init; }

        public required Vector3Dto SunAmbience { get; init; }

        public required Vector3Dto SunColor { get; init; }

        public required Vector3Dto ShadowFillColor { get; init; }

        public required Vector4Dto SpecularColor { get; init; }

        public required float Bloom { get; init; }

        public required Vector3Dto FogColor { get; init; }

        public required float FogStart { get; init; }

        public required float FogEnd { get; init; }
    }
}
