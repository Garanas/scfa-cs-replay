namespace FAForever.MapEditor.Project
{
    /// <summary>
    /// The files of a map project, relative to its folder. Every image is a PNG that any image editor
    /// or terrain tool reads and writes; the sizes follow from the map's size in ogrids (width by
    /// height, in <see cref="Manifest"/>).
    /// </summary>
    /// <remarks>
    /// <code>
    /// map.json                    the settings of the .scmap (MapProjectManifest)
    /// props.json                  the props
    /// decals.json                 the decals and their groups
    /// heightmap.png               16-bit grey, (width + 1) by (height + 1); or heightmap.r16
    /// terrain-types.png           8-bit grey, width by height: the code of each ogrid's terrain type
    /// normal-map.png              colour with alpha, as the .scmap stores it
    /// preview.png                 colour with alpha, 256 by 256
    /// stratum-masks/stratum-N.png 8-bit grey, all the same size: how much of layer N covers the terrain
    /// water/water-map.png         colour with alpha
    /// water/foam.png, flatness.png, depth-bias.png   8-bit grey, half the map's size
    /// map/                        the other files of the map folder: _scenario.lua, _save.lua, _script.lua, ...
    /// </code>
    /// </remarks>
    public static class MapProjectLayout
    {
        public const string Manifest = "map.json";
        public const string Props = "props.json";
        public const string Decals = "decals.json";

        public const string Heightmap = "heightmap.png";

        /// <summary>
        /// The heightmap as raw little-endian 16-bit samples, as World Machine and Gaea export it; read
        /// when there is no <see cref="Heightmap"/>.
        /// </summary>
        public const string HeightmapRaw = "heightmap.r16";

        public const string TerrainTypes = "terrain-types.png";
        public const string NormalMap = "normal-map.png";
        public const string Preview = "preview.png";
        public const string StratumMasks = "stratum-masks";
        public const string WaterMap = "water/water-map.png";
        public const string WaterFoam = "water/foam.png";
        public const string WaterFlatness = "water/flatness.png";
        public const string WaterDepthBias = "water/depth-bias.png";

        /// <summary>
        /// The folder with the map's other files, copied as they are (the Lua files get the map's
        /// folder in their paths, see <see cref="MapPacker"/>).
        /// </summary>
        public const string MapFiles = "map";

        /// <summary>
        /// The mask of terrain layer <paramref name="layer"/> (1 to 8; 1 to 4 in version 53). Layer 0
        /// covers the whole terrain and layer 9 is drawn over everything, so neither has a mask.
        /// </summary>
        public static string StratumMask(int layer) => $"{StratumMasks}/stratum-{layer}.png";
    }
}
