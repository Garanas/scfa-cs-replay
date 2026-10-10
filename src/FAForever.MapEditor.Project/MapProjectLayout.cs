namespace FAForever.MapEditor.Project
{
    /// <summary>
    /// The files of a map project, relative to its folder. The JSON files hold the records of
    /// <c>Files.Serialized</c>; every image is a PNG that any image editor or terrain tool reads and
    /// writes, its size following from the map's size in ogrids.
    /// </summary>
    /// <remarks>
    /// <code>
    /// map.json                     name, version, scenario, terrain settings, lighting, water, sky, minimap
    /// props.json, decals.json      the props; the decals and their groups
    /// markers.json                 spawns, deposits, path nodes, AI markers, weather, areas, chains
    /// armies.json                  the armies and the units they start with
    /// heightmap.png                16-bit grey, (width + 1) by (height + 1); or heightmap.r16
    /// terrain-types.png            8-bit grey, width by height: the code of each ogrid's terrain type
    /// normal-map.png, preview.png  colour with alpha; the preview is 256 by 256
    /// stratum-masks/stratum-N.png  8-bit grey, all the same size: how much of layer N covers the terrain
    /// water/water-map.png          colour with alpha
    /// water/foam.png, flatness.png, depth-bias.png   8-bit grey, half the map's size
    /// masks/NAME.png               8-bit grey, the map's size or half: masks for tools (K-1), not exported
    /// script.lua                   the map's script, if it has one
    /// files/                       the other files of the map folder, as they are
    /// procedural/sources/          the map maker's World Machine and Gaea files (.tmd, .terrain)
    /// procedural/temp/             what those tools build, made again from the sources; not versioned
    /// .gitignore, .gitattributes   written when version control is turned on (ProjectRepository)
    /// </code>
    /// </remarks>
    public static class MapProjectLayout
    {
        public const string Map = "map.json";
        public const string Props = "props.json";
        public const string Decals = "decals.json";
        public const string Markers = "markers.json";
        public const string Armies = "armies.json";

        public const string Heightmap = "heightmap.png";

        /// <summary>
        /// The heightmap as raw little-endian 16-bit samples, as World Machine and Gaea export it;
        /// read when there is no <see cref="Heightmap"/>.
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
        public const string Script = "script.lua";

        /// <summary>
        /// The folder of the masks for tools (user story K-1), one 8-bit grey PNG per mask, named
        /// after it. The editor and other tools read them; the export leaves them out.
        /// </summary>
        public const string Masks = "masks";

        /// <summary>
        /// The folder with the other files of the map folder, copied as they are.
        /// </summary>
        public const string Files = "files";

        /// <summary>
        /// The folder of the procedural work: <see cref="ProceduralSources"/> and <see cref="ProceduralTemp"/>.
        /// The editor reads nothing in it by itself.
        /// </summary>
        public const string Procedural = "procedural";

        /// <summary>
        /// The files of World Machine, Gaea and other tools that make the terrain: kept with the project.
        /// </summary>
        public const string ProceduralSources = "procedural/sources";

        /// <summary>
        /// What those tools build (heightmaps, masks at any size): made again from the sources, so
        /// left out of version control.
        /// </summary>
        public const string ProceduralTemp = "procedural/temp";

        public const string GitIgnore = ".gitignore";
        public const string GitAttributes = ".gitattributes";

        /// <summary>
        /// The mask of terrain layer <paramref name="layer"/>: 1 to 8, or 1 to 4 in version 53.
        /// </summary>
        public static string StratumMask(int layer) => $"{StratumMasks}/stratum-{layer}.png";

        /// <summary>
        /// The file of the mask for tools named <paramref name="name"/>.
        /// </summary>
        public static string Mask(string name) => $"{Masks}/{name}.png";
    }
}
