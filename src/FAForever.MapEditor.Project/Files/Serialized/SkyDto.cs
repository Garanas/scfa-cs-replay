namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// The background, the cubemaps and the sky dome.
    /// </summary>
    public sealed record SkyDto
    {
        public required string Background { get; init; }

        public required string Cubemap { get; init; }

        /// <summary>
        /// The cubemaps that units reflect, by name.
        /// </summary>
        public required IReadOnlyList<CubemapDto> EnvironmentCubemaps { get; init; }

        /// <summary>
        /// The sky dome; only version 60 has one.
        /// </summary>
        public SkyboxDto? Skybox { get; init; }
    }
}
