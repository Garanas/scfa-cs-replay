namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// The terrain apart from its images (heightmap, types, masks, normal map, preview), which are
    /// files of their own whose sizes follow from <see cref="Width"/> and <see cref="Height"/>.
    /// </summary>
    public sealed record TerrainDto
    {
        /// <summary>
        /// The version of the <c>.scmap</c> to write: 53, 56 or 60.
        /// </summary>
        public required int ScmapVersion { get; init; }

        /// <summary>
        /// The width in ogrids, e.g. 256 for a 5 km map.
        /// </summary>
        public required int Width { get; init; }

        /// <summary>
        /// The height in ogrids.
        /// </summary>
        public required int Height { get; init; }

        /// <summary>
        /// The height of one step of the heightmap, usually 1/128.
        /// </summary>
        public required float HeightScale { get; init; }

        /// <summary>
        /// The terrain shader, usually <c>TTerrain</c> or <c>TTerrainXP</c>.
        /// </summary>
        public required string Shader { get; init; }

        /// <summary>
        /// The tileset of a version 53 map; absent in later versions.
        /// </summary>
        public string? Tileset { get; init; }

        /// <summary>
        /// The layers, lowest first: ten from version 56 on.
        /// </summary>
        public required IReadOnlyList<TerrainLayerDto> Layers { get; init; }

        /// <summary>
        /// How the normal map is stored in the <c>.scmap</c>.
        /// </summary>
        public required TextureFormat NormalMapFormat { get; init; }

        /// <summary>
        /// The width in ogrids of the tiles the imported map stored its normal map in, while the
        /// normal map is the imported one; absent for the engine's tiles (the map's size, at most 2048).
        /// </summary>
        public int? NormalMapTileWidth { get; init; }

        /// <summary>
        /// The height of those tiles.
        /// </summary>
        public int? NormalMapTileHeight { get; init; }
    }
}
