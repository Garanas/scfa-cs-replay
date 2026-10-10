using Avalonia.Controls;

namespace FAForever.MapEditor.App.Controls;

/// <summary>
/// A mode panel's list of what there is to edit, its data context a <see cref="ViewModels.PanelParts"/>:
/// a button per entry under its group's heading; the chosen one is filled, and the inspector shows it
/// (<c>Views/MapPanels/PanelPartView</c>).
/// </summary>
public sealed partial class PanelPartList : UserControl
{
    public PanelPartList()
    {
        InitializeComponent();
    }
}
