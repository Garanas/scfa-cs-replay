using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.App.Plugins;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The Plugins mode's list (the panel lists, the inspector edits): a fixed first entry about all
/// plugins, then an entry per plugin under its heading (its category, else the mode its tools join,
/// else Other), the headings in alphabetical order with Other last. The list follows every load,
/// reload, plugin added, removed or renamed (<see cref="PanelParts.SyncGroup"/>).
/// </summary>
public sealed partial class PluginsViewModel
{
    /// <summary>
    /// The id of the list's first entry, about every plugin (<c>view.inspectPart</c>).
    /// </summary>
    public const string OverviewPart = "plugins.overview";

    /// <summary>
    /// The heading of the plugins without a category or tools.
    /// </summary>
    public const string OtherGroup = "Other";

    /// <summary>
    /// The id of a plugin's entry: <c>plugins.plugin.&lt;folder&gt;</c>.
    /// </summary>
    public static string PartOf(string plugin) => $"plugins.plugin.{plugin}";

    // the mode the tools of a plugin joined when it was last loaded, by its folder, remembered in the
    // user's settings: a plugin turned off keeps its heading, also after a restart
    private readonly Dictionary<string, string> _toolModes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private bool _toolModesChanged;

    // the headings of plugins the list holds now, in its order
    private readonly List<string> _headings = [];

    /// <summary>
    /// The Plugins mode's list of entries (<c>Views/PluginPanels/PluginsList</c> in the panel, the
    /// chosen one in the inspector).
    /// </summary>
    public PanelParts Parts { get; }

    /// <summary>
    /// The heading a plugin is listed under: its category, else the mode its tools join (the last
    /// time it was loaded), else Other.
    /// </summary>
    public string HeadingOf(LuaPlugin plugin)
    {
        if (plugin.IsOn && plugin.IsLoaded)
        {
            // what it registered now is what it gives; a plugin that lost its tools forgets the mode
            if (plugin.Tools.Count > 0)
            {
                if (!_toolModes.TryGetValue(plugin.Id, out string? known) || known != plugin.Tools[0].Mode)
                {
                    _toolModes[plugin.Id] = plugin.Tools[0].Mode;
                    _toolModesChanged = true;
                }
            }
            else
            {
                _toolModesChanged |= _toolModes.Remove(plugin.Id);
            }
        }
        if (plugin.Manifest.Category is string category)
        {
            return category;
        }
        return _toolModes.TryGetValue(plugin.Id, out string? mode) && EditorModes.All.FirstOrDefault(m => m.Id == mode) is ModeDefinition definition
            ? definition.Title
            : OtherGroup;
    }

    /// <summary>
    /// What a plugin's entry says beside its title: its errors, <c>off</c>, its warnings
    /// (<c>on · 1 warning</c>), or <c>on</c> with the first of its tools, checks and commands (<c>on · 1 tool</c>).
    /// </summary>
    public static string DetailOf(LuaPlugin plugin)
    {
        if (plugin.HasErrors)
        {
            return plugin.Errors.Count == 1 ? "1 error" : $"{plugin.Errors.Count} errors";
        }
        if (!plugin.IsOn)
        {
            return "off";
        }
        if (plugin.HasWarnings)
        {
            return plugin.Warnings.Count == 1 ? "on · 1 warning" : $"on · {plugin.Warnings.Count} warnings";
        }
        static string Count(int count, string what) => $"on · {count} {what}{(count == 1 ? "" : "s")}";
        return plugin.Tools.Count > 0 ? Count(plugin.Tools.Count, "tool")
            : plugin.Checks.Count > 0 ? Count(plugin.Checks.Count, "check")
            : plugin.Commands.Count > 0 ? Count(plugin.Commands.Count, "command")
            : "on";
    }

    /// <summary>
    /// Brings the list in step with the plugins: entries of plugins gone are taken out, new ones
    /// added under their heading, and those that stay show the plugin as it is now (after a reload
    /// it is another object). The chosen plugin stays chosen while it is there, also when it moved to
    /// another heading. Cheap when nothing changed.
    /// </summary>
    private void SyncParts()
    {
        if (Parts is null)
        {
            return;
        }
        using Diagnostics.Performance.Span measure = Diagnostics.Performance.Measure("plugins.list");
        string chosen = Parts.Chosen.Id;
        Dictionary<string, List<LuaPlugin>> byHeading = new Dictionary<string, List<LuaPlugin>>(StringComparer.Ordinal);
        foreach (LuaPlugin plugin in Items)
        {
            string heading = HeadingOf(plugin);
            plugin.ListedUnder = heading;
            if (!byHeading.TryGetValue(heading, out List<LuaPlugin>? list))
            {
                byHeading[heading] = list = [];
            }
            list.Add(plugin);
        }
        string[] headings = [.. byHeading.Keys
            .OrderBy(heading => heading == OtherGroup ? 1 : 0)
            .ThenBy(heading => heading, StringComparer.CurrentCultureIgnoreCase)];

        PanelPartEntry Entry(LuaPlugin plugin) => new PanelPartEntry(PartOf(plugin.Id), plugin.Manifest.Name, () => new PluginPart(this, plugin), DetailOf(plugin));

        // first take out what went, also from a heading a plugin left, so no id is under two headings
        foreach (string heading in _headings.ToList())
        {
            List<LuaPlugin> staying = byHeading.GetValueOrDefault(heading) ?? [];
            Parts.SyncGroup(heading, [.. staying
                .Where(plugin => Parts.Find(PartOf(plugin.Id)) is not PanelPart part || part.Group == heading)
                .Select(Entry)]);
        }
        // then every heading in its order, a new one after the one before it
        string after = PluginsMode.OverviewGroup;
        foreach (string heading in headings)
        {
            Parts.SyncGroup(heading, [.. byHeading[heading].Select(Entry)], after);
            after = heading;
        }
        _headings.Clear();
        _headings.AddRange(headings);
        if (_toolModesChanged)
        {
            _toolModesChanged = false;
            SaveSettings();
        }

        foreach (PanelPart part in Parts.Items)
        {
            if (part.Content is PluginPart content && Find(content.Plugin.Id) is LuaPlugin now && now != content.Plugin)
            {
                content.Plugin = now;
            }
        }
        Parts.Items[0].Detail = Items.Count == 1 ? "1 plugin" : $"{Items.Count} plugins";
        if (chosen != Parts.Chosen.Id && Parts.Find(chosen) is not null)
        {
            Parts.Choose(chosen);
        }
    }

    /// <summary>
    /// Shows a plugin's entry in the inspector (<c>plugins.manage {plugin}</c>).
    /// </summary>
    /// <exception cref="ArgumentException">There is no such plugin.</exception>
    public void Choose(string plugin)
    {
        LuaPlugin found = Find(plugin) ?? throw new ArgumentException($"There is no plugin '{plugin}'; {string.Join(", ", Items.Select(p => p.Id))}");
        Parts.Choose(PartOf(found.Id));
    }

    /// <summary>
    /// A button of the mode's panel or inspector (Show the folder, Reload, a plugin's command): runs
    /// the command of that id as a person does; what goes wrong shows in the status line.
    /// </summary>
    [RelayCommand]
    private Task RunButton(string id) => _model.TryAsync(() => _model.Commands.RunAsync(id, new JsonObject(), interactive: true));
}

/// <summary>
/// A plugin's entry in the Plugins mode's inspector: on or off, its version, kind and description,
/// its errors, a button per command, its fields and what it registered. It follows the plugin when
/// it is read again (another <see cref="LuaPlugin"/> of the same folder).
/// </summary>
public sealed partial class PluginPart(PluginsViewModel host, LuaPlugin plugin) : ObservableObject
{
    [ObservableProperty]
    public partial LuaPlugin Plugin { get; internal set; } = plugin;

    /// <summary>
    /// The on check box: turns the plugin over through <c>plugins.enable</c>.
    /// </summary>
    [RelayCommand]
    private Task Toggle() => host.ToggleCommand.ExecuteAsync(Plugin.Id);

    /// <summary>
    /// A command's button: runs it as one step of undo.
    /// </summary>
    [RelayCommand]
    private Task Run(string id) => host.RunButtonCommand.ExecuteAsync(id);
}

/// <summary>
/// The Plugins mode's first entry in the inspector: where the plugins are, how many, and how a
/// plugin says its category and kind.
/// </summary>
public sealed record PluginsOverviewPart(PluginsViewModel Plugins);
