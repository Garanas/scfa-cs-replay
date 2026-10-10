using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FAForever.MapEditor.App.ViewModels;

namespace FAForever.MapEditor.App.Views;

public sealed partial class NewMapPage : UserControl
{
    public NewMapPage()
    {
        InitializeComponent();
        // Enter in a field presses Create, as in a dialog (Escape closes the page: the window's)
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!e.Handled && e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && e.Source is TextBox
            && DataContext is MainViewModel { NewMapPage: { IsOpen: true } page } && page.CreateCommand.CanExecute(null))
        {
            page.CreateCommand.Execute(null);
            e.Handled = true;
        }
    }
}
