using FAForever.MapEditor.Project.Files.InMemory;

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
    /// sketch/images.json           the pictures projected onto the map (N-2), in drawing order; absent without images, never exported
    /// sketch/images/NAME.png       the picture of the projected image NAME, copied into the project (at most 4096 pixels on a side)
    /// stacks/heightmap.json        the heightmap's stack of layers (T-5), lowest first; absent without layers
    /// stacks/heightmap/NAME.png    16-bit grey, (width + 1) by (height + 1): a height layer's values
    /// stacks/heightmap/NAME.coverage.png   8-bit grey, the same size: how much of the layer there is, when not all
    /// stacks/TARGET.json, stacks/TARGET/   the stack of another grid (stratum-N, mask-NAME, water-foam), same shape
    /// script.lua                   the map's script, if it has one
    /// files/                       the other files of the map folder, as they are
    /// procedural/sources/          the map maker's World Machine and Gaea files (.tmd, .terrain)
    /// procedural/temp/             what those tools build, made again from the sources; not versioned
    /// .gitignore, .gitattributes   written when version control is turned on (ProjectRepository)
    /// </code>
    /// <para>
    /// Who writes a file shows in its name (format 7). A file whose name ends in <c>.gen</c> before
    /// its extension (<see cref="Generated"/>: <c>stratum-masks/stratum-2.gen.png</c>) is made by the
    /// editor from other things and written again whenever they change: the grid of a target with a
    /// stack of layers (what the stack makes), a rules or decal-masks layer's grid, the draft for World
    /// Machine and Gaea. Another tool may read it; what it writes into it is lost, and the editor does
    /// not read it back. Every other file is the map's own: the editor writes it when it is edited
    /// there (a painted layer, a mask without a stack), and reads back what another tool writes into
    /// it. A file an image layer reads (<c>procedural/temp/forest.png</c>) belongs to the tool that
    /// writes it: the editor never writes it, and reads it again when it changes.
    /// </para>
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
        /// The folder of the stacks of layers (format 6, <c>LayerStackDto</c>): per target a JSON
        /// file (<see cref="StackIndex"/>) and a folder of the layers' PNGs (<see cref="StackLayer"/>).
        /// With a stack, the target's own file (<see cref="Heightmap"/>) is what the stack makes,
        /// written by the editor; other tools write into a layer's file or an image layer's file.
        /// </summary>
        public const string Stacks = "stacks";

        /// <summary>
        /// The settings of the stack of a target, lowest first: <c>stacks/heightmap.json</c>.
        /// </summary>
        public static string StackIndex(string target) => $"{Stacks}/{target}.json";

        /// <summary>
        /// The values of a layer of a target's stack: <c>stacks/heightmap/Sculpt.png</c>.
        /// </summary>
        public static string StackLayer(string target, string name) => $"{Stacks}/{target}/{name}.png";

        /// <summary>
        /// The coverage of a layer of a stack of heights, when it has one: <c>stacks/heightmap/Road.coverage.png</c>.
        /// </summary>
        public static string StackLayerCoverage(string target, string name) => $"{Stacks}/{target}/{name}.coverage.png";

        /// <summary>
        /// The heightmap's stack: <c>stacks/heightmap.json</c>.
        /// </summary>
        public const string HeightStackIndex = "stacks/heightmap.json";

        /// <summary>
        /// The folder of the height layers of projects of format 3 to 5 (user story T-5):
        /// <see cref="HeightLayersIndex"/> and a PNG per layer, read into the heightmap's stack when
        /// such a project opens and removed by its first save.
        /// </summary>
        public const string HeightLayers = "layers";

        /// <summary>
        /// The height layers' settings in projects of format 3 to 5 (<c>HeightLayersDto</c>).
        /// </summary>
        public const string HeightLayersIndex = "layers/layers.json";

        /// <summary>
        /// The values of a height layer in projects of format 3 to 5.
        /// </summary>
        public static string HeightLayer(string name) => $"{HeightLayers}/{name}.png";

        /// <summary>
        /// The coverage of a height layer in projects of format 3 to 5, when it has one.
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
        /// What the name of a file the editor makes from other things ends with before its extension.
        /// </summary>
        public const string GeneratedMark = ".gen";

        /// <summary>
        /// The name a file has when the editor makes it from other things:
        /// <c>stratum-masks/stratum-2.png</c> becomes <c>stratum-masks/stratum-2.gen.png</c>.
        /// </summary>
        public static string Generated(string file)
        {
            string extension = Path.GetExtension(file);
            return file[..^extension.Length] + GeneratedMark + extension;
        }

        /// <summary>
        /// Whether a file's name says the editor makes it (<see cref="Generated"/>), whatever its folder.
        /// </summary>
        public static bool IsGenerated(string file) =>
            Path.GetFileNameWithoutExtension(file.Replace('\\', '/')).EndsWith(GeneratedMark, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// A file's name without <see cref="GeneratedMark"/>: <c>forest.gen.png</c> gives <c>forest.png</c>.
        /// </summary>
        public static string WithoutGenerated(string file)
        {
            if (!IsGenerated(file))
            {
                return file;
            }
            string extension = Path.GetExtension(file);
            return file[..^(GeneratedMark.Length + extension.Length)] + extension;
        }

        /// <summary>
        /// The file of the grid a stack of layers makes, as the map's own (without a stack):
        /// <c>heightmap.png</c>, <c>stratum-masks/stratum-2.png</c>, <c>masks/forest.png</c>, <c>water/foam.png</c>.
        /// </summary>
        /// <exception cref="ArgumentException">The target is no grid of a project.</exception>
        public static string GridFile(LayerTarget target)
        {
            if (target.IsHeights)
            {
                return Heightmap;
            }
            if (target.Key.StartsWith("stratum-", StringComparison.Ordinal))
            {
                return $"{StratumMasks}/{target.Key}.png";
            }
            if (target.Key.StartsWith("mask-", StringComparison.Ordinal))
            {
                return Mask(target.Key["mask-".Length..]);
            }
            return target.Key switch
            {
                "water-foam" => WaterFoam,
                "water-flatness" => WaterFlatness,
                "water-depth-bias" => WaterDepthBias,
                _ => throw new ArgumentException($"{target} is no grid of a project", nameof(target)),
            };
        }

        /// <summary>
        /// The file a target's grid is saved in: <see cref="Generated"/> while a stack of layers makes it
        /// (the heightmap with height layers, a grid with a stack of coverage), else the map's own.
        /// </summary>
        public static string GridFile(MapDocument document, LayerTarget target) =>
            IsMadeByStack(document, target) ? Generated(GridFile(target)) : GridFile(target);

        /// <summary>
        /// Whether a stack of layers makes a target's grid, so the editor writes its file.
        /// </summary>
        public static bool IsMadeByStack(MapDocument document, LayerTarget target) =>
            target.IsHeights ? !document.HeightStack.IsEmpty : document.CoverageStacks.ContainsKey(target.Key);

        /// <summary>
        /// Whether the editor makes a layer's grid from its source (rules, decal masks), so its file is
        /// <see cref="Generated"/>; a painted or image layer holds what was painted or read.
        /// </summary>
        public static bool IsGeneratedLayer(Layer layer) => layer.Source is RulesSource or DecalMasksSource;

        /// <summary>
        /// The file of a layer's values: <c>stacks/stratum-2/Paint.png</c>, <c>stacks/stratum-2/Rocks.gen.png</c>.
        /// </summary>
        public static string StackLayerFile(string target, Layer layer) =>
            IsGeneratedLayer(layer) ? Generated(StackLayer(target, layer.Name)) : StackLayer(target, layer.Name);

        /// <summary>
        /// The file of a layer's coverage (heights) or alpha (a painted layer of coverage), named as <see cref="StackLayerFile"/>.
        /// </summary>
        public static string StackLayerCoverageFile(string target, Layer layer) =>
            IsGeneratedLayer(layer) ? Generated(StackLayerCoverage(target, layer.Name)) : StackLayerCoverage(target, layer.Name);

        /// <summary>
        /// The file of the mask of terrain layer <paramref name="layer"/>: 1 to 8, or 1 to 4 in version 53.
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
        /// The pictures projected onto the map (user story N-2): their places, sizes, turns and looks,
        /// in the order they are drawn. Absent while the map has none. Part of the sketch, the
        /// project's own: never exported.
        /// </summary>
        public const string ImagesFile = "sketch/images.json";

        /// <summary>
        /// The folder of the projected images' pictures, a PNG each named after its image
        /// (<see cref="Image"/>). Absent while the map has none.
        /// </summary>
        public const string ImagesFolder = "sketch/images";

        /// <summary>
        /// The file of the picture of the projected image named <paramref name="name"/>.
        /// </summary>
        public static string Image(string name) => $"{ImagesFolder}/{name}.png";

        /// <summary>
        /// Which results kept outside the map (walkability) were current when the project was saved
        /// (<see cref="Generation.KeptResults"/>), generated again when it opens. Absent without any.
        /// </summary>
        public const string KeptResults = ".generated.json";

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
