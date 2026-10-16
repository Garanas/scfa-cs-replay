using System.Collections.Immutable;
using System.Numerics;

namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// The water: its shader settings, textures, wave generators and masks. The elevations are kept
    /// whether or not the map has water.
    /// </summary>
    public sealed record Water
    {
        public bool HasWater { get; init; }

        /// <summary>
        /// The height of the water surface.
        /// </summary>
        public float Elevation { get; init; }

        public float ElevationDeep { get; init; }

        public float ElevationAbyss { get; init; }

        public Vector3 SurfaceColor { get; init; }

        public Vector2 ColorLerp { get; init; }

        public float RefractionScale { get; init; }

        public float FresnelBias { get; init; }

        public float FresnelPower { get; init; }

        public float UnitReflection { get; init; }

        public float SkyReflection { get; init; }

        public float SunShininess { get; init; }

        public float SunStrength { get; init; }

        public Vector3 SunDirection { get; init; }

        public Vector3 SunColor { get; init; }

        public float SunReflection { get; init; }

        public float SunGlow { get; init; }

        public required string Cubemap { get; init; }

        public required string Ramp { get; init; }

        /// <summary>
        /// The four normal map layers of the surface.
        /// </summary>
        public ImmutableArray<WaveTexture> WaveTextures { get; init; } = [];

        public ImmutableArray<WaveGenerator> WaveGenerators { get; init; } = [];

        public required TiledBuffer<Color32> Map { get; init; }

        public required TextureFormat MapFormat { get; init; }

        /// <summary>
        /// One value per two by two ogrids (half the map's width by half its height).
        /// </summary>
        public required TiledBuffer<byte> FoamMask { get; init; }

        /// <inheritdoc cref="FoamMask"/>
        public required TiledBuffer<byte> FlatnessMask { get; init; }

        /// <inheritdoc cref="FoamMask"/>
        public required TiledBuffer<byte> DepthBiasMask { get; init; }
    }
}
