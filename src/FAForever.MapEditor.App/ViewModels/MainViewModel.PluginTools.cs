using System.ComponentModel;
using FAForever.MapEditor.App.Tools;

namespace FAForever.MapEditor.App.ViewModels;

public sealed partial class MainViewModel
{
    /// <summary>
    /// Adds a plugin's tool to a mode's tools, after its own (user story F-15): the mode's panel
    /// lists it at once when it shows.
    /// </summary>
    /// <exception cref="ArgumentException">There is no such mode, or the tool's id is taken.</exception>
    public void AddPluginTool(string modeId, ToolDefinition definition, IMapTool tool)
    {
        ModeItem mode = Modes.FirstOrDefault(m => m.Definition.Id == modeId)
            ?? throw new ArgumentException($"There is no mode '{modeId}': {string.Join(", ", Modes.Select(m => m.Definition.Id))}");
        if (!_tools.TryAdd(definition.Id, tool))
        {
            throw new ArgumentException($"There is a tool {definition.Id} already");
        }
        mode.PluginTools.Add(definition);
        if (tool is INotifyPropertyChanged settings)
        {
            settings.PropertyChanged += OnPluginToolChanged;
        }
        if (SelectedMode == mode)
        {
            RefreshTools(mode);
        }
    }

    /// <summary>
    /// Takes a plugin's tool away again (the plugin was turned off or is read again); while it was
    /// the active tool, its mode's first tool becomes active.
    /// </summary>
    /// <returns>Whether there was such a tool.</returns>
    public bool RemovePluginTool(string id)
    {
        if (Modes.FirstOrDefault(m => m.PluginTools.Any(tool => tool.Id == id)) is not ModeItem mode)
        {
            return false;
        }
        mode.PluginTools.RemoveAll(tool => tool.Id == id);
        if (_tools.Remove(id, out IMapTool? tool) && tool is INotifyPropertyChanged settings)
        {
            settings.PropertyChanged -= OnPluginToolChanged;
        }
        if (mode.LastTool == id)
        {
            mode.LastTool = null;
        }
        if (SelectedMode == mode)
        {
            RefreshTools(mode);
        }
        return true;
    }

    /// <summary>
    /// Makes the selected mode's list of tools again, keeping the active tool when it is still there.
    /// </summary>
    private void RefreshTools(ModeItem mode)
    {
        string? active = ActiveTool?.Definition.Id;
        Tools = [.. mode.AllTools.Select(tool => new ToolItem(tool, _tools.GetValueOrDefault(tool.Id)))];
        OnPropertyChanged(nameof(Tools));
        SelectTool(active is not null && mode.AllTools.Any(tool => tool.Id == active) ? active : mode.Definition.Tools[0].Id);
    }

    private void OnPluginToolChanged(object? sender, PropertyChangedEventArgs e) => CursorChanged?.Invoke(this, EventArgs.Empty);
}
