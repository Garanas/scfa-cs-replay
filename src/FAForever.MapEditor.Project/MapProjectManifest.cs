using FAForever.FileFormats.Map;

namespace FAForever.MapEditor.Project
{
    /// <summary>
    /// The settings of a map's <c>.scmap</c> that are not images: the project's <c>map.json</c>
    /// (<see cref="MapProjectLayout.Manifest"/>). The images, the props and the decals have files of
    /// their own; <see cref="MapProjectLayout"/> lists them.
    /// </summary>
    /// <remarks>
    /// The nested values are the records of <see cref="Scmap"/>, so a renamed member there renames a
    /// key in every project. <see cref="FormatVersion"/> is the version of this file's layout.
    /// </remarks>
    public sealed record MapProjectManifest
    {
        /// <summary>
        /// The layout of the project this manifest belongs to; <see cref="CurrentFormatVersion"/> for
        /// projects written by this library.
        /// </summary>
        public int FormatVersion { get; init; } = CurrentFormatVersion;

        public const int CurrentFormatVersion = 1;

        /// <summary>
        /// The version of the <c>.scmap</c> to write: 53, 56 or 60 (<see cref="Scmap.Version"/>).
        /// </summary>
        public required int ScmapVersion { get; init; }

        /// <summary>
        /// The width of the map in ogrids; the images follow from it (<see cref="MapProjectLayout"/>).
        /// </summary>
        public required int Width { get; init; }

        /// <summary>
        /// The height of the map in ogrids.
        /// </summary>
        public required int Height { get; init; }

        /// <summary>
        /// The height of one step of the heightmap, usually 1/128 (<see cref="ScmapHeightmap.Scale"/>).
        /// </summary>
        public required float HeightScale { get; init; }

        public required string TerrainShader { get; init; }

        public required string BackgroundTexture { get; init; }

        public required string SkyCubemap { get; init; }

        public required IReadOnlyList<ScmapCubemap> EnvironmentCubemaps { get; init; }

        public required ScmapLighting Lighting { get; init; }

        public required ScmapWater Water { get; init; }

        public required IReadOnlyList<ScmapWaveGenerator> WaveGenerators { get; init; }

        /// <inheritdoc cref="Scmap.Minimap"/>
        public ScmapMinimap? Minimap { get; init; }

        /// <inheritdoc cref="Scmap.Tileset"/>
        public string? Tileset { get; init; }

        /// <inheritdoc cref="Scmap.Layers"/>
        public required IReadOnlyList<ScmapTerrainLayer> Layers { get; init; }

        /// <inheritdoc cref="Scmap.Skybox"/>
        public ScmapSkybox? Skybox { get; init; }

        /// <summary>
        /// How the normal map is stored in the <c>.scmap</c>: DXT5 in nearly every map, uncompressed
        /// in some version 53 maps.
        /// </summary>
        public required ScmapTextureFormat NormalMapFormat { get; init; }

        /// <summary>
        /// How the water map is stored in the <c>.scmap</c> (DXT5 in every map of the vault).
        /// </summary>
        public required ScmapTextureFormat WaterMapFormat { get; init; }
    }

    /// <summary>
    /// The decals of a map with their groups: the project's <c>decals.json</c>.
    /// </summary>
    /// <param name="Header">The two numbers before the decals (<see cref="Scmap.DecalHeader"/>).</param>
    public sealed record MapProjectDecals(
        IReadOnlyList<int> Header,
        IReadOnlyList<ScmapDecal> Decals,
        IReadOnlyList<ScmapDecalGroup> Groups);

    /// <summary>
    /// The pixel formats of the textures in a <c>.scmap</c>, as the maps of the vault store them.
    /// </summary>
    public enum ScmapTextureFormat
    {
        /// <summary>
        /// Uncompressed, 32 bits per pixel (blue, green, red and alpha bytes).
        /// </summary>
        A8R8G8B8,

        /// <summary>
        /// Block compressed (BC3), with interpolated alpha.
        /// </summary>
        Dxt5,
    }
}
