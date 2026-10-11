using Avalonia.Controls;

namespace FAForever.MapEditor.App.Views.PluginPanels;

/// <summary>
/// The chosen plugin in the Plugins mode's inspector, its data context a <see cref="ViewModels.PluginPart"/>.
/// </summary>
public sealed partial class PluginPartView : UserControl
{
    public PluginPartView()
    {
        InitializeComponent();
    }
}
