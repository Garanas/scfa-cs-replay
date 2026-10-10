using FAForever.MapEditor.App.Tools;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The Procedural mode's list of what is generated from the map: per result how it stands, Generate,
/// its preview in the views, whether it regenerates on its own, and what the export does with it.
/// </summary>
public sealed record GeneratedResultsSection() : PanelSection("Generated results");

/// <summary>
/// Walkability's choices: the layer of units, what changed since the time before.
/// </summary>
public sealed record WalkabilitySection() : PanelSection("Walkability");

/// <summary>
/// The navigation mesh: the layer shown, the routes it measures, its regions and routes.
/// </summary>
public sealed record NavigationSection() : PanelSection("Navigation mesh");

/// <summary>
/// The steps of the export, as they stand in the export running or the last one.
/// </summary>
public sealed record ExportStepsSection() : PanelSection("Export");

/// <summary>
/// The Procedural mode (at the bottom of the rail): what is generated from the map, gathered in one
/// place. Its sections are listed here, apart from <see cref="EditorModes"/>, so the rail's entry
/// and its contents can be changed apart.
/// </summary>
public static class ProceduralMode
{
    public const string Id = "procedural";

    /// <summary>
    /// The mode's tools: Regions, which chooses a region of the navigation mesh; Path nodes, which
    /// edits the generated path networks by hand.
    /// </summary>
    public static IReadOnlyList<ToolDefinition> Tools { get; } =
    [
        new ToolDefinition("procedural.regions", "Regions", "T-8") { Make = model => new RegionTool(model) },
        new ToolDefinition("procedural.pathNodes", "Path nodes", "R-3") { Make = model => new PathNodeEditTool(model) },
    ];

    /// <summary>
    /// The mode's inspector: the chosen map-wide decal's settings.
    /// </summary>
    public static IReadOnlyList<PanelSection> Inspector { get; } = [new MapWideProceduralDetailsSection(), new SkyCubeDetailsSection()];

    /// <summary>
    /// The mode's panel: the generated results, the map-wide decals, walkability's choices, the
    /// navigation mesh, the path networks and the export's steps.
    /// </summary>
    public static IReadOnlyList<PanelSection> Sections { get; } =
    [
        new GeneratedResultsSection(),
        new MapWideDecalsProceduralSection(),
        new SkyCubeProceduralSection(),
        new WalkabilitySection(),
        new NavigationSection(),
        new PathNetworkSection(),
        new ExportStepsSection(),
    ];
}
