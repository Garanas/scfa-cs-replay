using Avalonia.Controls;

namespace FAForever.MapEditor.App.Views;

/// <summary>
/// The guided tours, over the window (Help, Tours; user story H-1): each tour with its length, a check once
/// finished, and a Start button; a tour file that cannot be used is listed with its error.
/// </summary>
public sealed partial class ToursPage : UserControl
{
    public ToursPage()
    {
        InitializeComponent();
    }
}
