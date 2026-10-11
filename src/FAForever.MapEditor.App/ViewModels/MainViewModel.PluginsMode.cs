namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The Plugins mode (user story F-15, <see cref="PluginsMode"/>): its list of plugins is one of the
/// mode panels' lists, so <c>view.inspectPart</c> chooses a plugin's entry
/// (<c>plugins.plugin.&lt;folder&gt;</c>) as any other.
/// </summary>
public sealed partial class MainViewModel
{
    private void InitializePluginsMode() => AddPanelPartList(PluginsMode.Id, Plugins.Parts);
}
