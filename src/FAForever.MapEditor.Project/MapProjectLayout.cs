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
    /// masks/NAME.rules.json        the rules a mask is generated from (K-2), when it has rules
    /// prop-rules.json              the rules that place props from the layers (P-4), when the map has rules
    /// layers/layers.json           the height layers (T-5), lowest first; absent for a map without them
    /// layers/NAME.png              16-bit grey, (width + 1) by (height + 1): a height layer's values
    /// layers/NAME.coverage.png     8-bit grey, the same size: how much of the layer there is, when not all
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
        /// The folder of the height layers (user story T-5): <see cref="HeightLayersIndex"/> and a
        /// PNG per layer. With layers, <see cref="Heightmap"/> is what they add up to, written by
        /// the editor; other tools write into a layer's file or an imported layer's source.
        /// </summary>
        public const string HeightLayers = "layers";

        /// <summary>
        /// The height layers' settings, lowest first (<c>HeightLayersDto</c>).
        /// </summary>
        public const string HeightLayersIndex = "layers/layers.json";

        /// <summary>
        /// The values of the height layer named <paramref name="name"/>.
        /// </summary>
        public static string HeightLayer(string name) => $"{HeightLayers}/{name}.png";

        /// <summary>
        /// The coverage of the height layer named <paramref name="name"/>, when it has one.
        /// </summary>
        public static string HeightLayerCoverage(string name) => $"{HeightLayers}/{name}.coverage.png";

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

        /// <summary>
        /// The rules that place props from the layers (user story P-4); absent while the map has none.
        /// </summary>
        public const string PropRules = "prop-rules.json";

        /// <summary>
        /// Where walkability is marked by hand (user story T-7), an ogrid a pixel: black none, grey
        /// blocked, white open (<see cref="Editing.WalkabilityMarks"/>). Absent without marks.
        /// </summary>
        public const string WalkabilityMarks = "walkability-marks.png";

        /// <summary>
        /// What the file of a mask's rules ends with, after the mask's name.
        /// </summary>
        public const string MaskRulesSuffix = ".rules.json";

        /// <summary>
        /// The file of the rules the mask for tools named <paramref name="name"/> is generated from (user story K-2).
        /// </summary>
        public static string MaskRules(string name) => $"{Masks}/{name}{MaskRulesSuffix}";

        /// <summary>
        /// Whether a file of the project (with <c>/</c> between folders) holds a mask's rules, and whose.
        /// </summary>
        public static bool IsMaskRules(string file, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? name)
        {
            name = null;
            if (!file.StartsWith(Masks + "/", StringComparison.Ordinal)
                || !file.EndsWith(MaskRulesSuffix, StringComparison.OrdinalIgnoreCase)
                || file.IndexOf('/', Masks.Length + 1) >= 0)
            {
                return false;
            }
            name = file[(Masks.Length + 1)..^MaskRulesSuffix.Length];
            return name.Length > 0;
        }
    }
}
