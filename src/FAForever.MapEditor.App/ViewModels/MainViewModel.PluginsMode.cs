namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The Plugins mode (user story F-15, <see cref="PluginsMode"/>): its list of plugins is one of the
/// mode panels' lists, so <c>view.inspectPart</c> chooses a plugin's entry
/// (<c>plugins.plugin.&lt;folder&gt;</c>) as any other.
/// </summary>
public sealed partial class MainViewModel
{
    private void InitializePluginsMode()
    {
        AddPanelPartList(PluginsMode.Id, Plugins.Parts);
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Tools))
            {
                OnPropertyChanged(nameof(ShowsToolTiles));
            }
        };
    }

    /// <summary>
    /// Whether the mode's panel shows its TOOLS: not when its only tool is Select (the Plugins mode
    /// without a plugin's tool), which stays the active tool, so the views select with it all the same.
    /// </summary>
    public bool ShowsToolTiles => !(Tools.Count == 1 && Tools[0].Definition.Id.EndsWith(".select", StringComparison.Ordinal));
}
