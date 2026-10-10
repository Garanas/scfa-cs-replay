using System.Collections.Immutable;

namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// A map as the editor holds it, in the editor's own terms: one immutable version of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A change makes a new document that shares everything it does not change with the old one:
    /// records with <c>with</c>, lists as immutable arrays, and grids (heightmap, masks, images) as
    /// <see cref="TiledBuffer{T}"/>s that share their unchanged tiles. Keeping old versions is
    /// therefore cheap, and <see cref="Editing.EditSession"/> keeps them for undo and redo.
    /// </para>
    /// <para>
    /// What follows from something else is not stored: the size comes from the heightmap, the paths
    /// of the map's files from <see cref="Name"/>, <see cref="FileName"/> and <see cref="Version"/>,
    /// and the decoration of markers (colour, editor prop) from their kind. <see cref="MapImporter"/>
    /// and <see cref="MapExporter"/> translate between a document and the game's files;
    /// <see cref="MapProjectStore"/> keeps a document on disk.
    /// </para>
    /// </remarks>
    public sealed record MapDocument
    {
        /// <summary>
        /// The name of the map's folder without its version, e.g. <c>theta_passage</c>.
        /// </summary>
        public required string Name { get; init; }

        /// <summary>
        /// The name the map's files start with: <c>&lt;FileName&gt;.scmap</c>, <c>&lt;FileName&gt;_save.lua</c>, ...
        /// </summary>
        public required string FileName { get; init; }

        /// <summary>
        /// The version of the map in the vault; the folder gets <c>.v0004</c> from it.
        /// </summary>
        public int? Version { get; init; }

        public MapFolderName Folder => new MapFolderName(Name, Version);

        /// <summary>
        /// The version an export that raises it gives the map (user story M-8): one more than its
        /// own, or 1 for a map without one.
        /// </summary>
        public int NextVersion => (Version ?? 0) + 1;

        /// <summary>
        /// The width of the map in ogrids, from the heightmap.
        /// </summary>
        public int Width => Terrain.Heightmap.Width;

        /// <summary>
        /// The height of the map in ogrids, from the heightmap.
        /// </summary>
        public int Height => Terrain.Heightmap.Height;

        public required ScenarioInfo Scenario { get; init; }

        public required Terrain Terrain { get; init; }

        public required Lighting Lighting { get; init; }

        public required Water Water { get; init; }

        public required Sky Sky { get; init; }

        /// <summary>
        /// The colours of the minimap; absent in version 53.
        /// </summary>
        public Minimap? Minimap { get; init; }

        /// <summary>
        /// The symmetry the editor mirrors edits by, kept with the project; null when none was chosen.
        /// </summary>
        public SymmetrySetting? Symmetry { get; init; }

        /// <summary>
        /// How the generated results are made (the Procedural mode): which regenerate on their own,
        /// which the export makes, the navigation mesh's settings. Kept with the project.
        /// </summary>
        public ProceduralSettings Procedural { get; init; } = ProceduralSettings.Default;

        public ImmutableArray<Decal> Decals { get; init; } = [];

        public ImmutableArray<DecalGroup> DecalGroups { get; init; } = [];

        /// <summary>
        /// The props, each with an <see cref="Prop.Id"/> no other prop of the document has.
        /// </summary>
        public ImmutableArray<Prop> Props { get; init; } = [];

        public required MarkerSet Markers { get; init; }

        public ImmutableArray<Army> Armies { get; init; } = [];

        /// <summary>
        /// The map's script (<c>_script.lua</c>) as its bytes; null for a map without one.
        /// </summary>
        public ImmutableArray<byte>? Script { get; init; }

        /// <summary>
        /// The other files of the map's folder (textures, Lua files the script loads, ...) by their
        /// path in the folder, with <c>/</c> between folders.
        /// </summary>
        public ImmutableSortedDictionary<string, ImmutableArray<byte>> Files { get; init; } =
            ImmutableSortedDictionary.Create<string, ImmutableArray<byte>>(StringComparer.Ordinal);

        /// <summary>
        /// The masks for tools (user story K-1: where rivers go, where forests grow) by name, each
        /// the map's size or half of it, from 0 (nothing) to 255 (full). They are the map maker's
        /// own and the editor's rules', kept in the project (<c>masks/&lt;name&gt;.png</c>) and never
        /// exported to the game. <see cref="Editing.MaskLayers"/> adds, renames and removes them.
        /// </summary>
        public ImmutableSortedDictionary<string, TiledBuffer<byte>> Masks { get; init; } =
            ImmutableSortedDictionary.Create<string, TiledBuffer<byte>>(StringComparer.Ordinal);

        /// <summary>
        /// The rules of the masks for tools that are made from rules (user story K-2), by the mask's
        /// name: such a mask's rules layer is generated from them when asked.
        /// </summary>
        /// <remarks>
        /// The rules live in the masks' stacks (<see cref="CoverageStacks"/>) as rules layers
        /// (<see cref="RulesSource"/>): this is, per mask with one, the rules of its lowest rules layer.
        /// </remarks>
        public ImmutableSortedDictionary<string, MaskRuleSet> MaskRules => Editing.CoverageLayers.MaskRulesOf(this);

        /// <summary>
        /// The stacks of layers of the grids of coverage (user stories K-1, K-2, S-1, W-2; the plan
        /// "Map Editor Rework"): stratum masks, masks for tools and the water's three maps, by their
        /// target's key (<see cref="LayerTarget.Key"/>: <c>stratum-2</c>, <c>mask-forest</c>,
        /// <c>water-foam</c>). A grid without a stack here is simply its own painted layer
        /// (<see cref="Editing.CoverageLayers.StackOf"/>); with one, the grid is what the stack makes,
        /// kept so by <see cref="Editing.CoverageLayers.Follow"/> after every change, so everything that
        /// reads the grids (the views, the rules, the export) reads the composite.
        /// </summary>
        public ImmutableSortedDictionary<string, LayerStack> CoverageStacks { get; init; } =
            ImmutableSortedDictionary.Create<string, LayerStack>(StringComparer.Ordinal);

        /// <summary>
        /// The rules that place props from the layers when asked (user story P-4), in the order they
        /// place; their props carry the rule's name (<see cref="Prop.Rule"/>). Kept as <c>prop-rules.json</c>.
        /// </summary>
        public ImmutableArray<PropRule> PropRules { get; init; } = [];

        /// <summary>
        /// The playable area at the start (user story M-3): the area <see cref="ScenarioInfo.PlayableArea"/>
        /// names, else the first area (the map editors' convention: their script makes <c>AREA_1</c>
        /// playable); null for a map without areas, which is playable as a whole.
        /// </summary>
        public Area? PlayableArea => Scenario.PlayableArea is string name && Markers.Areas.FirstOrDefault(area => area.Name == name) is Area named
            ? named
            : Markers.Areas.FirstOrDefault();

        /// <summary>
        /// The heightmap's stack of layers (user stories T-5, T-4; <see cref="LayerStack"/>), lowest
        /// first: the base, then painted (sculpt), image and curve layers. Empty for a map without
        /// them, whose heightmap is simply its heights. With layers, <see cref="Terrain"/>'s heightmap
        /// is what they make, kept so by <see cref="Editing.HeightLayers.Follow"/> after every change;
        /// everything that reads the heights (the views, walkability, the export) reads that.
        /// </summary>
        public LayerStack HeightStack { get; init; } = LayerStack.Empty(LayerTarget.Heightmap);

        /// <summary>
        /// The draft heightmap's stack of layers (the rough shape blocked out, and what World Machine
        /// or Gaea get as input): its own order of layers, many of them shared with
        /// <see cref="HeightStack"/>, the final (<see cref="Editing.HeightStacks"/>). Empty exactly when
        /// the final is: a map with layers always has both; an older project opens with every layer
        /// in both, so the two make the same terrain.
        /// </summary>
        public LayerStack DraftStack { get; init; } = LayerStack.Empty(LayerTarget.Heightmap);

        /// <summary>
        /// The draft heightmap's samples, what <see cref="DraftStack"/> makes (kept so by
        /// <see cref="Editing.HeightLayers.Follow"/>), at the heightmap's size and scale; null while
        /// the draft makes the same heights as the final (both stacks hold the same layers in the
        /// same order), which <see cref="DraftHeightmap"/> then gives.
        /// </summary>
        public TiledBuffer<ushort>? DraftSamples { get; init; }

        /// <summary>
        /// The draft heightmap: <see cref="DraftSamples"/> at the heightmap's scale, or the heightmap
        /// itself while the draft makes the same heights. Never exported: the export, walkability,
        /// the normal map and every other generator read <see cref="Terrain"/>'s heightmap, the final.
        /// </summary>
        public Heightmap DraftHeightmap => DraftSamples is TiledBuffer<ushort> samples && !ReferenceEquals(samples, Terrain.Heightmap.Samples)
            ? Terrain.Heightmap with { Samples = samples }
            : Terrain.Heightmap;

        /// <summary>
        /// Where the map maker marked by hand that land units must be blocked or may pass (user story
        /// T-7), one value an ogrid (<see cref="Editing.WalkabilityMarks"/>: none, blocked, open); null
        /// for a map without marks. Walkability takes them over what it generates; the export writes
        /// the blocked ones as a blocking terrain type. Kept as <c>walkability-marks.png</c>.
        /// </summary>
        public TiledBuffer<byte>? WalkabilityMarks { get; init; }

        /// <summary>
        /// Whether the map may be uploaded to the vault. An upload (user story M-8) refuses a map
        /// for which this is false; exporting to the game's maps folder works for every map.
        /// </summary>
        public bool IsPublishable => Scenario.Publishable;

        /// <summary>
        /// An id for a new prop: one more than the highest in use.
        /// </summary>
        public int NextPropId() => Props.IsEmpty ? 1 : Props.Max(prop => prop.Id) + 1;
    }
}
