using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.App.Plugins;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tools;

/// <summary>
/// A tool a Lua plugin registered (<c>plugin.tool{...}</c>, user story F-15): the left mouse in the
/// views calls the plugin's <c>press</c>, <c>drag</c> and <c>release</c> with the place in ogrids,
/// and what they edit through the plugin API goes into one stroke, shown while the mouse is down
/// and one step of undo when it is let go (<see cref="PluginsViewModel"/> runs them). Its options
/// row shows the plugin's fields it names.
/// </summary>
public sealed partial class PluginTool : ObservableObject, IMapTool
{
    private readonly PluginsViewModel _host;

    internal PluginTool(PluginsViewModel host, LuaPlugin plugin, PluginToolSpec spec, IReadOnlyList<Setting> settings)
    {
        _host = host;
        Plugin = plugin;
        Spec = spec;
        Settings = settings;
    }

    public LuaPlugin Plugin { get; }

    public PluginToolSpec Spec { get; }

    public string Title => Spec.Title;

    /// <summary>
    /// What the plugin says the tool does, shown in its options row.
    /// </summary>
    public string Description => Spec.Description ?? "";

    public bool HasDescription => !string.IsNullOrEmpty(Spec.Description);

    /// <summary>
    /// The plugin's fields its options row shows, as the Plugins page has them.
    /// </summary>
    public IReadOnlyList<Setting> Settings { get; }

    /// <summary>
    /// The plugin's radius: a number, or the value of one of its fields.
    /// </summary>
    public double CursorRadius => Spec.RadiusField is string key ? _host.NumberOf(Plugin, key) : Spec.Radius;

    /// <summary>
    /// The edit the mouse holds while it is down; null otherwise.
    /// </summary>
    internal EditTransaction? Stroke { get; set; }

    /// <summary>
    /// The run the plugin's handlers edit through during the stroke.
    /// </summary>
    internal PluginRun? Run { get; set; }

    /// <summary>
    /// The map before the stroke, to tell whether it changed.
    /// </summary>
    internal MapDocument? Before { get; set; }

    /// <summary>
    /// Where the mouse was last, for <c>release</c>.
    /// </summary>
    internal (float X, float Z) Last { get; set; }

    /// <summary>
    /// Says that a field changed: the radius or a setting may read otherwise.
    /// </summary>
    internal void Refresh(MapDocument? document)
    {
        foreach (Setting setting in Settings)
        {
            setting.Refresh(document);
        }
        OnPropertyChanged(nameof(CursorRadius));
    }

    public void Press(float x, float z) => _host.ToolPress(this, x, z, ToolInput.Default);

    public void Press(float x, float z, ToolInput input) => _host.ToolPress(this, x, z, input);

    public void Drag(float x, float z) => _host.ToolDrag(this, x, z, ToolInput.Default);

    public void Drag(float x, float z, ToolInput input) => _host.ToolDrag(this, x, z, input);

    public void Release() => _host.ToolRelease(this);
}
