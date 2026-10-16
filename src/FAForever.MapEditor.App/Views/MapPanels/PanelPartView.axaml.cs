using Avalonia.Controls;

namespace FAForever.MapEditor.App.Views.MapPanels;

/// <summary>
/// The inspector's part of the entry chosen in a mode panel's list, its data context a
/// <see cref="ViewModels.PanelParts"/>.
/// </summary>
public sealed partial class PanelPartView : UserControl
{
    public PanelPartView()
    {
        InitializeComponent();
    }
}
