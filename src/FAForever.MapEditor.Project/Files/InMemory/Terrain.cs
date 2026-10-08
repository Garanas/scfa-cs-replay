using System.Collections.Immutable;

namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// The terrain: its heights, its types, and the layers of texture that cover it.
    /// </summary>
    public sealed record Terrain
    {
        /// <summary>
        /// The version of the <c>.scmap</c> to write: 53 (Supreme Commander), 56 (Forged Alliance)
        /// or 60 (FAF map editor, with a skybox).
        /// </summary>
        public required int ScmapVersion { get; init; }

        public required Heightmap Heightmap { get; init; }

        /// <summary>
        /// The terrain type of every ogrid, width by height, an index into the game's terrain types
        /// (<c>lua/TerrainTypes.lua</c>).
        /// </summary>
        public required TiledBuffer<byte> Types { get; init; }

        /// <summary>
        /// The terrain shader, usually <c>TTerrain</c> or <c>TTerrainXP</c>.
        /// </summary>
        public required string Shader { get; init; }

        /// <summary>
        /// The tileset of a version 53 map; absent in later versions.
        /// </summary>
        public string? Tileset { get; init; }

        /// <summary>
        /// The layers, lowest first: ten from version 56 on, the last drawn over everything.
        /// </summary>
        public ImmutableArray<TerrainLayer> Layers { get; init; } = [];

        public required StratumMasks StratumMasks { get; init; }

        public required TiledBuffer<Color32> NormalMap { get; init; }

        public required TextureFormat NormalMapFormat { get; init; }

        /// <summary>
        /// The preview the <c>.scmap</c> starts with, 256 by 256 pixels.
        /// </summary>
        public required TiledBuffer<Color32> Preview { get; init; }
    }
}
