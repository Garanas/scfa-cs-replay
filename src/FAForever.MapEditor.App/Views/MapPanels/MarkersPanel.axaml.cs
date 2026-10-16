using Avalonia.Controls;

namespace FAForever.MapEditor.App.Views.MapPanels;

/// <summary>
/// The Markers panel's list, its data context the <see cref="ViewModels.MainViewModel"/>: the kinds,
/// layers of path nodes, chains and the adaptive map (<see cref="ViewModels.MarkersViewModel.Parts"/>).
/// </summary>
public sealed partial class MarkersPanel : UserControl
{
    public MarkersPanel()
    {
        InitializeComponent();
    }
}
