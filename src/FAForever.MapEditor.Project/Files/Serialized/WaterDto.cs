namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// The water apart from its images (water map, foam, flatness and depth bias), which are files
    /// of their own. The elevations are kept whether or not the map has water.
    /// </summary>
    public sealed record WaterDto
    {
        public required bool HasWater { get; init; }

        /// <summary>
        /// The height of the water surface.
        /// </summary>
        public required float Elevation { get; init; }

        public required float ElevationDeep { get; init; }

        public required float ElevationAbyss { get; init; }

        public required Vector3Dto SurfaceColor { get; init; }

        public required Vector2Dto ColorLerp { get; init; }

        public required float RefractionScale { get; init; }

        public required float FresnelBias { get; init; }

        public required float FresnelPower { get; init; }

        public required float UnitReflection { get; init; }

        public required float SkyReflection { get; init; }

        public required float SunShininess { get; init; }

        public required float SunStrength { get; init; }

        public required Vector3Dto SunDirection { get; init; }

        public required Vector3Dto SunColor { get; init; }

        public required float SunReflection { get; init; }

        public required float SunGlow { get; init; }

        public required string Cubemap { get; init; }

        public required string Ramp { get; init; }

        /// <summary>
        /// The four normal map layers of the surface.
        /// </summary>
        public required IReadOnlyList<WaveTextureDto> WaveTextures { get; init; }

        public required IReadOnlyList<WaveGeneratorDto> WaveGenerators { get; init; }

        /// <summary>
        /// How the water map is stored in the <c>.scmap</c>.
        /// </summary>
        public required TextureFormat MapFormat { get; init; }
    }
}
