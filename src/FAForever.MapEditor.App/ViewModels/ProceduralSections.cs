using FAForever.MapEditor.App.Tools;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The Procedural mode's list (the panel lists, the inspector edits): an entry per generated result
/// with how it stands, a picture and Generate, under headings, and the export's steps last
/// (<see cref="ProceduralListViewModel"/>).
/// </summary>
public sealed record ProceduralListSection() : PanelSection("Generated results");

/// <summary>
/// The Procedural inspector's part of the chosen entry: its settings and details (<see cref="ProceduralPart"/>).
/// </summary>
public sealed record ProceduralDetailsSection() : PanelSection("Generated result");

/// <summary>
/// The Procedural mode (at the bottom of the rail): what is generated from the map, gathered in one
/// place. Its sections are listed here, apart from <see cref="EditorModes"/>, so the rail's entry
/// and its contents can be changed apart.
/// </summary>
public static class ProceduralMode
{
    public const string Id = "procedural";

    /// <summary>
    /// The mode's tools: Connected regions, which chooses a region of the navigation mesh; Path nodes, which
    /// edits the generated path networks by hand.
    /// </summary>
    public static IReadOnlyList<ToolDefinition> Tools { get; } =
    [
        new ToolDefinition("procedural.regions", "Connected regions", "T-8") { Make = model => new RegionTool(model) },
        new ToolDefinition("procedural.pathNodes", "Path nodes", "R-3") { Make = model => new PathNodeEditTool(model) },
    ];

    /// <summary>
    /// The mode's inspector: the chosen entry's settings and details.
    /// </summary>
    public static IReadOnlyList<PanelSection> Inspector { get; } = [new ProceduralDetailsSection()];

    /// <summary>
    /// The mode's panel: the list of generated results, the export last.
    /// </summary>
    public static IReadOnlyList<PanelSection> Sections { get; } = [new ProceduralListSection()];
}
