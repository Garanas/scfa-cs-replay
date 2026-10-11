using Avalonia.Controls;

namespace FAForever.MapEditor.App.Views.PluginPanels;

/// <summary>
/// The Plugins mode's first entry in the inspector, its data context a <see cref="ViewModels.PluginsOverviewPart"/>.
/// </summary>
public sealed partial class PluginsOverviewView : UserControl
{
    public PluginsOverviewView()
    {
        InitializeComponent();
    }
}
