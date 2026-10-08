using Avalonia.Controls;
using Avalonia.Interactivity;
using FAForever.MapEditor.App.Controls;
using FAForever.MapEditor.App.ViewModels;

namespace FAForever.MapEditor.App.Views;

public sealed partial class MainWindow : Window
{
    private bool _mayClose;

    public MainWindow()
    {
        InitializeComponent();
        Heightmap.CursorMoved += text => CursorText.Text = text;
    }

    /// <summary>
    /// The view of the map, for automation.
    /// </summary>
    public HeightmapView MapView => Heightmap;

    /// <summary>
    /// Closes the window without asking about unsaved changes, for automation.
    /// </summary>
    public void CloseWithoutAsking()
    {
        _mayClose = true;
        Close();
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_mayClose || DataContext is not MainViewModel model || model.Workspace?.Session.IsModified != true)
        {
            return;
        }

        // ask first, then close for real
        e.Cancel = true;
        if (await model.ConfirmCloseAsync())
        {
            _mayClose = true;
            Close();
        }
    }

    private void OnFit(object? sender, RoutedEventArgs e) => Heightmap.Fit();
}
