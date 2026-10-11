using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.Project.Editing;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The Plugins mode's panel (user story F-15): Show the folder and Reload, then the plugins under
/// their headings (<see cref="PluginsViewModel.Parts"/>).
/// </summary>
public sealed record PluginsListSection() : PanelSection("Plugins");

/// <summary>
/// The Plugins mode's inspector: the chosen plugin (<see cref="PluginPart"/>), or every plugin's
/// overview (<see cref="PluginsOverviewPart"/>).
/// </summary>
public sealed record PluginDetailsSection() : PanelSection("Plugin");

/// <summary>
/// The Plugins mode (at the bottom of the rail, after Procedural): the workspace's Lua plugins,
/// listed by category, the chosen one in the inspector. Its sections are listed here, apart from
/// <see cref="EditorModes"/>, so the rail's entry and its contents can be changed apart.
/// </summary>
public static class PluginsMode
{
    public const string Id = "plugins";

    /// <summary>
    /// The heading of the list's first entry, above the plugins' categories.
    /// </summary>
    public const string OverviewGroup = "Workspace";

    /// <summary>
    /// The mode's own tool: Select, for the things a plugin's command acts on
    /// (<c>editor.selection()</c>); plugins' tools with <c>mode = "plugins"</c> join after it.
    /// </summary>
    public static IReadOnlyList<ToolDefinition> Tools { get; } =
    [
        new ToolDefinition("plugins.select", "Select", "F-11") { Make = model => new SelectTool(model, ObjectKinds.All) },
    ];

    /// <summary>
    /// The mode's inspector: the chosen entry.
    /// </summary>
    public static IReadOnlyList<PanelSection> Inspector { get; } = [new PluginDetailsSection()];

    /// <summary>
    /// The mode's panel: the list of plugins.
    /// </summary>
    public static IReadOnlyList<PanelSection> Sections { get; } = [new PluginsListSection()];
}
