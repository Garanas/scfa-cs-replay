using Avalonia.Controls;

namespace FAForever.MapEditor.App.Views.PluginPanels;

/// <summary>
/// The Plugins mode's panel, its data context the <see cref="ViewModels.PluginsViewModel"/>: the
/// folder's buttons and the plugins under their headings.
/// </summary>
public sealed partial class PluginsList : UserControl
{
    public PluginsList()
    {
        InitializeComponent();
    }
}
