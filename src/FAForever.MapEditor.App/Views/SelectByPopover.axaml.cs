using Avalonia.Controls;
using Avalonia.Input;
using FAForever.MapEditor.App.ViewModels;

namespace FAForever.MapEditor.App.Views;

/// <summary>
/// The Select by popover (user story U-10), over the top view; see <c>SelectByPopover.axaml</c>.
/// Escape, while a control of it has the keyboard, hides it.
/// </summary>
public sealed partial class SelectByPopover : UserControl
{
    public SelectByPopover()
    {
        InitializeComponent();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape && DataContext is SelectByViewModel { IsOpen: true } popover)
        {
            popover.Close();
            e.Handled = true;
        }
    }
}
