using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// A tool of a mode: what the mouse does in the views while it is active.
/// </summary>
/// <param name="Id">E.g. <c>terrain.curve</c>; the command <c>tool.terrain.curve</c> chooses it.</param>
/// <param name="Story">The user story it serves.</param>
/// <param name="Missing">What it is to do, while it is not built; null once it is.</param>
public sealed record ToolDefinition(string Id, string Title, string Story, string? Missing = null)
{
    public bool IsIncomplete => Missing is not null;

    /// <summary>
    /// Makes what it does, once per editor (<c>Tools/</c>); null while it is not built.
    /// </summary>
    public Func<MainViewModel, IMapTool>? Make { get; init; }

    /// <summary>
    /// The command that chooses it.
    /// </summary>
    public string CommandId => $"tool.{Id}";
}

/// <summary>
/// A part of a mode's panel or of the inspector.
/// </summary>
public abstract record PanelSection(string Title)
{
    /// <summary>
    /// The tools (by id) whose inspector shows it; null for every tool of the mode. A part of the
    /// mode's panel with it shows only while one of them is active.
    /// </summary>
    public IReadOnlyList<string>? ForTools { get; init; }

    /// <summary>
    /// The entries of the mode panel's list (<see cref="PanelParts"/>, by id) while one of which is
    /// chosen it shows; null whichever is chosen. The Lighting panel lists the sky's presets while
    /// its sky is chosen.
    /// </summary>
    public IReadOnlyList<string>? ForParts { get; init; }
}

/// <summary>
/// A part that is not built yet: what it will hold, and its user story.
/// </summary>
public sealed record PlannedSection(string Title, string Story, string Missing) : PanelSection(Title);

/// <summary>
/// The masks for tools of the map (user story K-1), the Masks mode's list: each with a preview;
/// add, rename, duplicate, delete, choose the one to paint, show and edit.
/// </summary>
public sealed record MasksSection() : PanelSection("Masks");

/// <summary>
/// The chosen mask's stack of layers (user stories K-1, K-2), the Masks mode's inspector: the
/// stack control, what a mask layer reads and a rules layer's rules.
/// </summary>
public sealed record MaskStackSection() : PanelSection("Mask");

/// <summary>
/// The overlays the mode's panel lists, to show or hide them there as with the chips above the
/// views (<see cref="OverlayDefinition.Panel"/>).
/// </summary>
public sealed record OverlaySection() : PanelSection("Shown in the views");

/// <summary>
/// The Sketch mode's list of projected images (user story N-2): each with its eye and opacity, and a button that
/// projects another picture; the chosen one is edited in the inspector (<see cref="ProjectedImageDetailsSection"/>).
/// </summary>
public sealed record ProjectedImagesSection() : PanelSection("Projected images");

/// <summary>
/// The chosen projected image (user story N-2), the Sketch mode's inspector: its picture's size, opacity, place, size and turn,
/// fits to the map, what it looks like, and replacing or removing it. Nothing while no image is chosen.
/// </summary>
public sealed record ProjectedImageDetailsSection() : PanelSection("Image");

/// <summary>
/// The Units mode's palettes (user story U-8): the chosen palette's ten slots, each a unit or a
/// units preset the keys 1 to 0 pick for the place tool, and the units placed last.
/// </summary>
public sealed record UnitPaletteSection() : PanelSection("Palette");

/// <summary>
/// A mode: one button on the rail, its tools and its panel; its parts of the inspector.
/// </summary>
/// <param name="Icon">The key of its icon in <c>Styles/Icons.axaml</c>, e.g. <c>Icon.Terrain</c>.</param>
/// <param name="Group">Modes of one group sit together on the rail, with a line between groups.</param>
/// <param name="Gesture">The shortcut of the command that chooses it, e.g. <c>F2</c>.</param>
/// <param name="Story">The user story of the mode itself.</param>
public sealed record ModeDefinition(
    string Id,
    string Title,
    string Icon,
    string Help,
    int Group,
    string Gesture,
    string Story,
    IReadOnlyList<ToolDefinition> Tools,
    IReadOnlyList<PanelSection> Sections,
    IReadOnlyList<PanelSection> Inspector)
{
    /// <summary>
    /// The command that chooses it: <c>terrain.mode</c>.
    /// </summary>
    public string CommandId => $"{Id}.mode";
}

/// <summary>
/// The editor's modes (concept A of the UI design), each with the user stories it will hold. A
/// plugin adds its own (user story F-15).
/// </summary>
public static class EditorModes
{
    // what the brush presets of the Terrain and Paint modes say while there are none
    private const string BrushPresetsEmpty = "None yet: save the brush's settings (shape, mode, radius, strength) to use them again.";

    /// <summary>
    /// The group of the modes at the bottom of the rail: Procedural and Plugins.
    /// </summary>
    public const int BottomGroup = 2;

    public static IReadOnlyList<ModeDefinition> All { get; } =
    [
        new ModeDefinition("map", "Map", "Icon.Map", "Information, armies, areas, script", 0, "F1", "M-1",
            [
                new ToolDefinition("map.areas", "Areas", "M-5") { Make = model => new AreaTool(model) },
                new ToolDefinition("map.size", "Map size", "M-3") { Make = model => new MapSizeTool(model) },
                new ToolDefinition("map.select", "Select", "F-11") { Make = model => new SelectTool(model, ObjectKinds.All) },
                new ToolDefinition("map.preset", "Preset", "F-10") { Make = model => new SelectionPresetTool(model) },
            ],
            [
                // the list of what the map has (wave 14A), the chosen entry in the inspector
                new MapPartsSection(),
                new PresetSection(SelectionPresets.Kind, "Selection presets") { EmptyText = "None yet: select things of any kind, such as a civilian base's units, props, decals and markers, and save them to place them again.", SaveLabel = "Save the selection as a preset" },
            ],
            [new MapDetailsSection()]),

        new ModeDefinition("terrain", "Terrain", "Icon.Terrain", "Height layers, sculpting, curves, terrain types, walkability", 0, "F2", "T-1",
            [
                new ToolDefinition("terrain.sculpt", "Sculpt", "T-1") { Make = model => new SculptTool(model) },
                new ToolDefinition("terrain.stamp", "Stamp", "T-2") { Make = model => new StampTool(model) },
                new ToolDefinition("terrain.curve", "Curve", "T-4") { Make = model => new CurveTool(model) },
                new ToolDefinition("terrain.types", "Terrain types", "T-6") { Make = model => new TerrainTypeTool(model) },
                new ToolDefinition("terrain.walkability", "Walkability", "T-7") { Make = model => new WalkabilityMarkTool(model) },
                new ToolDefinition("terrain.ruler", "Ruler", "V-5") { Make = model => new RulerTool(model) },
            ],
            [
                new HeightLayersSection(),
                new PresetSection(BrushPreset.Kind, "Brush presets") { EmptyText = BrushPresetsEmpty, SaveLabel = "Save the brush's settings as a preset", ForTools = ["terrain.sculpt", "terrain.curve", "terrain.types", "terrain.walkability", "terrain.ruler"] },
                new PresetSection(BrushPreset.StampKind, "Stamp presets") { EmptyText = "None yet: save the stamp's picture, size, height, mode, base and edge to press it again.", SaveLabel = "Save the stamp's settings as a preset", ForTools = ["terrain.stamp"] },
            ],
            [new HeightLayerSection()]),

        new ModeDefinition("paint", "Paint", "Icon.Paint", "Strata: their textures and their masks as stacks of layers", 0, "F3", "S-1",
            [
                new ToolDefinition("paint.strata", "Strata", "S-1") { Make = model => new StrataTool(model) },
            ],
            [
                new LayerTexturesSection(),
                new PresetSection(BrushPreset.Kind, "Brush presets") { EmptyText = BrushPresetsEmpty, SaveLabel = "Save the brush's settings as a preset" },
            ],
            // the chosen stratum: its textures and its mask's stack
            [new LayerSection { ForTools = ["paint.strata"] }]),

        // the masks for tools (wave 9B): each a stack of layers; never exported
        new ModeDefinition("masks", "Masks", "Icon.Masks", "Where things may happen: masks for tools, props and strata", 0, "Shift+F3", "K-1",
            [
                new ToolDefinition("masks.paint", "Mask brush", "K-1") { Make = model => new MaskPaintTool(model) },
            ],
            [
                new MasksSection(),
                new PresetSection(BrushPreset.Kind, "Brush presets") { EmptyText = BrushPresetsEmpty, SaveLabel = "Save the brush's settings as a preset" },
            ],
            [new MaskStackSection()]),

        new ModeDefinition("water", "Water", "Icon.Water", "Levels, colours, waves, paintable water", 0, "F4", "W-1",
            [
                new ToolDefinition("water.paint", "Paint water", "W-2") { Make = model => new WaterPaintTool(model) },
                new ToolDefinition("water.waves", "Waves", "W-1") { Make = model => new WaveTool(model) },
            ],
            [new WaterSection(), new PresetSection("water", "Water presets") { EmptyText = "None yet: save the water's levels, colours and waves to use them on another map.", SaveLabel = "Save the water settings as a preset" }],
            [new WaterDetailsSection()]),

        new ModeDefinition("lighting", "Lighting", "Icon.Lighting", "Sun, colours, fog, sky", 0, "F5", "L-1",
            [
                new ToolDefinition("lighting.sun", "Sun", "L-1") { Make = model => new SunTool(model) },
            ],
            [
                new LightingSection(),
                new PresetSection("lighting", "Lighting presets")
                {
                    EmptyText = "None yet: save the sun, colours and fog to use them on another map.", SaveLabel = "Save the lighting as a preset",
                    ForParts = [LightingSettingsViewModel.SunPart, LightingSettingsViewModel.ColoursPart, LightingSettingsViewModel.FogPart, LightingSettingsViewModel.BloomPart, LightingSettingsViewModel.ShadowDecalPart],
                },
                new PresetSection(PresetKinds.Sky, "Sky presets")
                {
                    EmptyText = "None yet: save the sky's cube maps, background and skybox to use them on another map.", SaveLabel = "Save the sky as a preset",
                    ForParts = [LightingSettingsViewModel.SkyPart, LightingSettingsViewModel.SkyboxPart],
                },
            ],
            [new LightingDetailsSection()]),

        new ModeDefinition("markers", "Markers", "Icon.Markers", "Spawns, deposits, path nodes, AI markers, chains, adaptive maps", 1, "F6", "R-1",
            [
                new ToolDefinition("markers.select", "Select", "F-11") { Make = model => new SelectTool(model, ObjectKinds.Markers | ObjectKinds.PathNodes | ObjectKinds.Areas) },
                new ToolDefinition("markers.spawn", "Spawn", "R-1") { Make = model => new PlaceMarkerTool(model, MarkerPlacement.Spawn) },
                new ToolDefinition("markers.mass", "Mass", "R-1") { Make = model => new PlaceMarkerTool(model, MarkerPlacement.Mass) },
                new ToolDefinition("markers.hydrocarbon", "Hydrocarbon", "R-1") { Make = model => new PlaceMarkerTool(model, MarkerPlacement.Hydrocarbon) },
                new ToolDefinition("markers.path", "Path node", "R-3") { Make = model => new PathTool(model) },
                new ToolDefinition("markers.ai", "AI marker", "R-1") { Make = model => new PlaceMarkerTool(model, MarkerPlacement.Ai) },
                new ToolDefinition("markers.blank", "Blank", "R-1") { Make = model => new PlaceMarkerTool(model, MarkerPlacement.Blank) },
                new ToolDefinition("markers.chain", "Chain", "R-4") { Make = model => new ChainTool(model) },
                new ToolDefinition("markers.weatherDefinition", "Weather", "R-1") { Make = model => new PlaceMarkerTool(model, MarkerPlacement.WeatherDefinition) },
                new ToolDefinition("markers.weatherGenerator", "Clouds", "R-1") { Make = model => new PlaceMarkerTool(model, MarkerPlacement.WeatherGenerator) },
                new ToolDefinition("markers.preset", "Preset", "F-10") { Make = model => new MarkerPresetTool(model) },
            ],
            [
                // the kinds, layers of path nodes, chains and the adaptive map as a list (wave 14B), presets last
                new MarkerListSection(),
                new PresetSection(MarkerPresets.Kind, "Marker presets") { EmptyText = "None yet: select markers, such as a spawn with its deposits, and save them to place them again.", SaveLabel = "Save the selected markers as a preset" },
            ],
            // the entry chosen in the list: a kind's or layer's counts, Select and Clear; a chain; the adaptive map
            [new MarkerDetailsSection()]),

        new ModeDefinition("props", "Props", "Icon.Props", "Place and paint props, props from rules, reclaim", 1, "F7", "P-1",
            [
                new ToolDefinition("props.select", "Select", "F-11") { Make = model => new SelectTool(model, ObjectKinds.Props) },
                new ToolDefinition("props.place", "Place", "P-1") { Make = model => new PropPlaceTool(model) },
                new ToolDefinition("props.paint", "Paint", "P-2") { Make = model => new PropPaintTool(model) },
                new ToolDefinition("props.erase", "Erase", "P-2") { Make = model => new PropEraseTool(model) },
            ],
            [
                new PropPlacementSection(),
                new PropPaletteSection(),
                new PropRulesSection(),
                new PropReclaimSection(),
                new PresetSection(PropsViewModel.PaletteKind, "Palettes") { EmptyText = "None yet: save the painting palette to paint with the same props again.", SaveLabel = "Save the palette as a preset" },
            ],
            [new PropRuleSection()]),

        new ModeDefinition("decals", "Decals", "Icon.Decals", "Place decals, groups", 1, "F8", "D-1",
            [
                new ToolDefinition("decals.select", "Select", "D-1") { Make = model => new DecalSelectTool(model) },
                new ToolDefinition("decals.place", "Place", "D-1") { Make = model => new DecalPlaceTool(model) },
            ],
            [new MapWideDecalsSection(), new DecalGroupsSection()],
            [new MapWideDecalDetailsSection()]),

        new ModeDefinition("units", "Units", "Icon.Units", "Place units, groups per army, presets", 1, "F9", "U-2",
            [
                new ToolDefinition("units.place", "Place", "U-2") { Make = model => new UnitPlaceTool(model) },
                new ToolDefinition("units.select", "Select", "F-11") { Make = model => new SelectTool(model, ObjectKinds.Units) },
            ],
            [
                // the palette's slots before the tree (U-8): what the keys 1 to 0 place, then where it goes
                new UnitPaletteSection(),
                new UnitGroupsSection(),
                new PresetSection(UnitPreset.Kind, "Unit presets") { EmptyText = "None yet: select units, or choose a group in the tree, and save them to place them again.", SaveLabel = "Save the selected units as a preset" },
            ],
            // the army or group chosen in the tree: its fields and buttons
            [new UnitGroupDetailsSection()]),

        // the Sketch mode (N-1, N-2, N-4): drafts of the map maker's own, kept in the project and never exported
        new ModeDefinition("sketch", "Sketch", "Icon.Sketch", "Draft the map: pencil strokes, symbols, labels and projected images; kept in the project, never exported", 1, "F10", "N-1",
            [
                new ToolDefinition("sketch.pencil", "Pencil", "N-1") { Make = model => new PencilTool(model) },
                new ToolDefinition("sketch.eraser", "Eraser", "N-1") { Make = model => new EraserTool(model) },
                new ToolDefinition("sketch.symbol", "Symbol", "N-4") { Make = model => new SymbolTool(model) },
                new ToolDefinition("sketch.label", "Label", "N-4") { Make = model => new LabelTool(model) },
                new ToolDefinition("sketch.select", "Select", "N-4") { Make = model => new SketchSelectTool(model) },
                new ToolDefinition("sketch.image", "Image", "N-2") { Make = model => new ImageTool(model) },
            ],
            [
                new SketchSheetsSection(),
                new ProjectedImagesSection(),
            ],
            // the sheet or the projected image chosen in the panel
            [new SketchSheetSection(), new ProjectedImageDetailsSection()]),

        // the Procedural mode (wave 8C): its tools and sections are ProceduralMode's
        new ModeDefinition(ProceduralMode.Id, "Procedural", "Icon.Procedural", "Generated results, connected regions, export steps", BottomGroup, "F11", "F-7",
            ProceduralMode.Tools,
            ProceduralMode.Sections,
            ProceduralMode.Inspector),

        // the Plugins mode (wave 14C): its tools and sections are PluginsMode's
        new ModeDefinition(PluginsMode.Id, "Plugins", "Icon.Plugins", "The workspace's Lua plugins: on or off, their commands, fields and errors", BottomGroup, "F12", "F-15",
            PluginsMode.Tools,
            PluginsMode.Sections,
            PluginsMode.Inspector),
    ];
}

/// <summary>
/// Something the views can show over the terrain: a chip above the views, and a command in the
/// View menu that turns it on or off.
/// </summary>
/// <param name="CommandTitle">What the View menu says, e.g. <c>Show markers</c>.</param>
/// <param name="Missing">What it is to show, while the views cannot; null once they can.</param>
public sealed record OverlayDefinition(string Id, string Title, string CommandTitle, string Story, string? Missing = null)
{
    public bool IsIncomplete => Missing is not null;

    /// <summary>
    /// The mode whose panel lists it too (an <see cref="OverlaySection"/>, or the check box of the heading of its title in the Markers panel's list), e.g. <c>markers</c>.
    /// </summary>
    public string? Panel { get; init; }

    /// <summary>
    /// The token of the colour it lays over the terrain, shown as a swatch on its chip (its legend),
    /// e.g. <c>MapBlockedBrush</c>; null for an overlay of symbols.
    /// </summary>
    public string? Swatch { get; init; }

    /// <summary>
    /// The shortcut of the command that turns it on or off, e.g. <c>Ctrl+Shift+K</c>; null for none.
    /// </summary>
    public string? Gesture { get; init; }

    /// <summary>
    /// The command that turns it on or off: <c>view.overlay.markers</c>.
    /// </summary>
    public string CommandId => $"view.overlay.{Id}";
}

/// <summary>
/// What the views can show over the terrain, in the order of the chips and the View menu.
/// </summary>
public static class EditorOverlays
{
    public static IReadOnlyList<OverlayDefinition> All { get; } =
    [

        new OverlayDefinition("markers", "Markers", "Show markers", "R-6") { Panel = "markers" },
        new OverlayDefinition("pathNodes", "Path nodes", "Show path nodes", "R-6") { Panel = "markers" },
        new OverlayDefinition("camera", "3D camera", "Show the 3D camera", "V-1"),
        // the sketch lies over every mode's terrain; after the three chips that keep their room at 1280 pixels
        new OverlayDefinition("sketch", "Sketch", "Show the sketch", "N-1") { Gesture = "Ctrl+Shift+K" },
        new OverlayDefinition("walkability", "Walkability", "Show walkability", "T-7") { Swatch = "MapBlockedBrush" },
        new OverlayDefinition("mask", "Mask", "Show the chosen mask", "K-1") { Swatch = "MapMaskBrush" },
        new OverlayDefinition("symmetry", "Symmetry", "Show symmetry", "F-12"),
        new OverlayDefinition("grid", "Grid", "Show the grid", "V-3"),
        new OverlayDefinition("areas", "Areas", "Show areas", "M-5") { Swatch = "MapAreaBrush" },
        new OverlayDefinition("props", "Props", "Show props", "V-2"),
        new OverlayDefinition("units", "Units", "Show units", "V-2"),
        new OverlayDefinition("armyColours", "Army colours", "Show the units in their army's colour in the top view", "U-10"),
        new OverlayDefinition("decals", "Decals", "Show decals", "V-3"),
        new OverlayDefinition("navigation", "Connected regions", "Show the connected regions: where units of a layer can path between", "T-8") { Swatch = "MapRegion1Brush" },
        new OverlayDefinition("generatedPathNodes", "Generated nodes", "Show the generated path nodes", "R-3") { Panel = "procedural" },
        new OverlayDefinition("images", "Images", "Show the projected images in the top view", "N-2") { Gesture = "Ctrl+Shift+J" },
    ];
}
