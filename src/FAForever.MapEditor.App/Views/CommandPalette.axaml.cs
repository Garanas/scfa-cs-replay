using Avalonia.Controls;
using Avalonia.Input;
using FAForever.MapEditor.App.ViewModels;

namespace FAForever.MapEditor.App.Views;

/// <summary>
/// The command palette: typing filters the commands, the arrows choose, Enter runs, Escape or a
/// click beside it closes.
/// </summary>
public sealed partial class CommandPalette : UserControl
{
    public CommandPalette()
    {
        InitializeComponent();
    }

    private MainViewModel? Model => DataContext as MainViewModel;

    /// <summary>
    /// Puts the keyboard in the search box.
    /// </summary>
    public void FocusQuery() => Query.Focus();

    private async void OnQueryKeyDown(object? sender, KeyEventArgs e)
    {
        if (Model is not MainViewModel model)
        {
            return;
        }
        switch (e.Key)
        {
            case Key.Down or Key.Up:
                int index = Math.Clamp(Results.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, Math.Max(0, model.PaletteResults.Count - 1));
                Results.SelectedIndex = index;
                Results.ScrollIntoView(index);
                e.Handled = true;
                break;
            case Key.Enter:
                e.Handled = true;
                await model.RunPaletteSelectionAsync();
                break;
            case Key.Escape:
                model.IsPaletteOpen = false;
                e.Handled = true;
                break;
        }
    }

    private async void OnResultChosen(object? sender, TappedEventArgs e)
    {
        if (Model is MainViewModel model)
        {
            await model.RunPaletteSelectionAsync();
        }
    }

    private void OnScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Model is MainViewModel model)
        {
            model.IsPaletteOpen = false;
        }
    }

    // a click on the palette itself keeps it open
    private void OnDialogPressed(object? sender, PointerPressedEventArgs e) => e.Handled = true;
}
