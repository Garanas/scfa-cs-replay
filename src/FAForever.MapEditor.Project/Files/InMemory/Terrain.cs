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
        /// or 60 (what the engine saves, with a skybox: the game's own maps and the FAF map editor's).
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

        /// <summary>
        /// The normal map, one image (a map stored in tiles is imported as their whole). The game
        /// does not read it: it makes the normal map from the heightmap.
        /// </summary>
        public required TiledBuffer<Color32> NormalMap { get; init; }

        public required TextureFormat NormalMapFormat { get; init; }

        /// <summary>
        /// The width in ogrids of the tiles the imported map stored its normal map in, kept while the
        /// normal map is the imported one (<see cref="FileFormats.Map.Scmap.NormalMapTileWidth"/>); null
        /// writes the engine's tiles (the map's size, at most 2048).
        /// </summary>
        public int? NormalMapTileWidth { get; init; }

        /// <summary>
        /// The height of those tiles; see <see cref="NormalMapTileWidth"/>.
        /// </summary>
        public int? NormalMapTileHeight { get; init; }

        /// <summary>
        /// The preview the <c>.scmap</c> starts with, 256 by 256 pixels.
        /// </summary>
        public required TiledBuffer<Color32> Preview { get; init; }
    }
}
