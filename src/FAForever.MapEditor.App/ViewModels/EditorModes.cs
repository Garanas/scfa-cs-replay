using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.Project.Editing;

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
public abstract record PanelSection(string Title);

/// <summary>
/// A part that is not built yet: what it will hold, and its user story.
/// </summary>
public sealed record PlannedSection(string Title, string Story, string Missing) : PanelSection(Title);

/// <summary>
/// The terrain layers whose masks the strata tool paints (user story S-1).
/// </summary>
public sealed record StrataSection() : PanelSection("Strata");

/// <summary>
/// The overlays the mode's panel lists, to show or hide them there as with the chips above the
/// views (<see cref="OverlayDefinition.Panel"/>).
/// </summary>
public sealed record OverlaySection() : PanelSection("Shown in the views");

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
    public static IReadOnlyList<ModeDefinition> All { get; } =
    [
        new ModeDefinition("map", "Map", "Icon.Map", "Information, armies, areas, script", 0, "F1", "M-1",
            [
                new ToolDefinition("map.areas", "Areas", "M-5", "Draw, name, move and resize areas"),
            ],
            [
                new PlannedSection("Information", "M-1", "Name, description, author, version, type, preview, no-rush radius, adaptive, reclaim"),
                new ArmiesSection(),
                new PlannedSection("Areas", "M-5", "The areas of the map, by name"),
                new PlannedSection("Script and files", "M-6", "The map's script and the files of its folder"),
            ],
            [new PlannedSection("Map", "M-1", "The map's information, or the selected army or area")]),

        new ModeDefinition("terrain", "Terrain", "Icon.Terrain", "Height layers, sculpting, curves, terrain types, walkability", 0, "F2", "T-1",
            [
                new ToolDefinition("terrain.sculpt", "Sculpt", "T-1") { Make = model => new SculptTool(model) },
                new ToolDefinition("terrain.curve", "Curve", "T-4", "Draw a curve and shape the terrain along it: ridge, valley, road, cliff edge"),
                new ToolDefinition("terrain.types", "Terrain types", "T-6", "Paint the terrain type of each ogrid"),
                new ToolDefinition("terrain.walkability", "Walkability", "T-7", "Mark where land units must be blocked or may pass"),
                new ToolDefinition("terrain.ruler", "Ruler", "V-5", "Measure distances and heights"),
            ],
            [
                new PresetSection("brush", "Brush presets"),
                new PlannedSection("Height layers", "T-5", "Layers that add up to the heightmap: base, sculpting, curves, results of World Machine or Gaea"),
                new PlannedSection("Generated from the layers", "T-7", "Walkability, with what changed since it was generated last"),
            ],
            [new PlannedSection("Layer or curve", "T-5", "The selected height layer or curve: how it combines, strength, mask, points")]),

        new ModeDefinition("paint", "Paint", "Icon.Paint", "Strata and masks for tools", 0, "F3", "S-1",
            [
                new ToolDefinition("paint.strata", "Strata", "S-1") { Make = model => new StrataTool(model) },
                new ToolDefinition("paint.masks", "Masks", "K-1", "Paint the masks for World Machine, Gaea and the rules"),
            ],
            [
                new StrataSection(),
                new PresetSection("brush", "Brush presets"),
                new PlannedSection("Layer textures", "S-2", "Each layer's albedo and normal texture and their scales; add, remove, reorder"),
                new PlannedSection("Masks for tools", "K-1", "Named masks for one's own purposes, saved as PNG"),
            ],
            [new PlannedSection("Layer", "S-2", "The selected layer's textures and scales, or the selected mask")]),

        new ModeDefinition("water", "Water", "Icon.Water", "Levels, colours, waves, paintable water", 0, "F4", "W-1",
            [
                new ToolDefinition("water.paint", "Paint water", "W-2", "Paint foam, flatness and depth bias"),
            ],
            [new PlannedSection("Water", "W-1", "On or off; elevations; colours, sun, reflection; textures; waves")],
            [new PlannedSection("Water", "W-1", "The water's settings")]),

        new ModeDefinition("lighting", "Lighting", "Icon.Lighting", "Sun, colours, fog, sky", 0, "F5", "L-1",
            [
                new ToolDefinition("lighting.sun", "Sun", "L-1", "Turn the sun with a handle in the 3D view"),
            ],
            [
                new PlannedSection("Lighting", "L-1", "Sun direction, colours, ambience, shadow fill, multiplier, specular, bloom, fog, sky"),
                new PlannedSection("Presets", "L-2", "The lighting of a biome as a preset"),
            ],
            [new PlannedSection("Lighting", "L-1", "The lighting's settings")]),

        new ModeDefinition("markers", "Markers", "Icon.Markers", "Spawns, deposits, path nodes, AI markers, chains, adaptive maps", 1, "F6", "R-1",
            [
                new ToolDefinition("markers.select", "Select", "F-11") { Make = model => new SelectTool(model, ObjectKinds.Markers | ObjectKinds.PathNodes | ObjectKinds.Areas) },
                new ToolDefinition("markers.spawn", "Spawn", "R-1", "Place spawns"),
                new ToolDefinition("markers.mass", "Mass", "R-1", "Place mass deposits"),
                new ToolDefinition("markers.hydrocarbon", "Hydrocarbon", "R-1", "Place hydrocarbon deposits"),
                new ToolDefinition("markers.path", "Path node", "R-3", "Place path nodes and connect them"),
                new ToolDefinition("markers.ai", "AI marker", "R-1", "Place AI markers by kind"),
                new ToolDefinition("markers.chain", "Chain", "R-4", "Click markers in order to make a chain"),
            ],
            [
                new OverlaySection(),
                new PlannedSection("By kind", "R-6", "The markers by kind, each kind shown or hidden"),
                new PlannedSection("Chains", "R-4", "The chains, and their markers in order"),
            ],
            [new PlannedSection("Markers", "R-2", "The selected markers' properties")]),

        new ModeDefinition("props", "Props", "Icon.Props", "Place and paint props, props from rules, reclaim", 1, "F7", "P-1",
            [
                new ToolDefinition("props.place", "Place", "P-1", "Place, move, rotate, scale and delete props"),
                new ToolDefinition("props.paint", "Paint", "P-2", "Paint a palette of props, only where chosen layers are (P-3)"),
                new ToolDefinition("props.erase", "Erase", "P-2", "Take props away with a brush"),
            ],
            [
                new PlannedSection("Palettes", "P-2", "Props with weights, density, random scale and rotation"),
                new PlannedSection("Rules", "P-4", "Props from layers: where they may grow and where not; generated when asked"),
                new PlannedSection("Reclaim", "P-5", "The mass and energy of the props"),
            ],
            [new PlannedSection("Props", "P-1", "The selected props, palette or rule")]),

        new ModeDefinition("decals", "Decals", "Icon.Decals", "Place decals, groups", 1, "F8", "D-1",
            [
                new ToolDefinition("decals.place", "Place", "D-1", "Place decals from the picker, move, rotate and scale them"),
            ],
            [new PlannedSection("Decal groups", "D-2", "Groups of decals, shown or hidden")],
            [new PlannedSection("Decals", "D-1", "The selected decals: textures, size, distances, order")]),

        new ModeDefinition("units", "Units", "Icon.Units", "Place units, groups per army, presets", 1, "F9", "U-2",
            [
                new ToolDefinition("units.place", "Place", "U-2") { Make = model => new UnitPlaceTool(model) },
                new ToolDefinition("units.select", "Select", "F-11") { Make = model => new SelectTool(model, ObjectKinds.Units) },
            ],
            [
                new UnitGroupsSection(),
                new PresetSection(UnitPreset.Kind, "Presets") { EmptyText = "None yet: select units, or choose a group in the tree, and save them to place them again." },
            ],
            [new PlannedSection("Units", "U-3", "The selected group or units")]),
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
    /// The mode whose panel lists it too (<see cref="OverlaySection"/>), e.g. <c>markers</c>.
    /// </summary>
    public string? Panel { get; init; }

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
        new OverlayDefinition("walkability", "Walkability", "Show walkability", "T-7", "Show where land units can and cannot go, and what changed"),
        new OverlayDefinition("mask", "Mask", "Show a mask", "K-1", "Show a mask over the terrain"),
        new OverlayDefinition("symmetry", "Symmetry", "Show symmetry", "F-12"),
        new OverlayDefinition("grid", "Grid", "Show the grid", "V-3", "Show a grid over the terrain"),
        new OverlayDefinition("areas", "Areas", "Show areas", "M-5"),
        new OverlayDefinition("props", "Props", "Show props", "V-2"),
        new OverlayDefinition("units", "Units", "Show units", "V-2"),
    ];
}
