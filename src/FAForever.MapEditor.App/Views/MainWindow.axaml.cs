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
    /// The view of the map from above, for automation.
    /// </summary>
    public HeightmapView MapView => Heightmap;

    /// <summary>
    /// The view of the map in 3D, for automation.
    /// </summary>
    public TerrainView3D View3D => Terrain3D;

    /// <summary>
    /// Which view shows: <c>top</c> or <c>3d</c>.
    /// </summary>
    public string ShownView
    {
        get => Views.SelectedIndex == 1 ? "3d" : "top";
        set => Views.SelectedIndex = value.Equals("3d", StringComparison.OrdinalIgnoreCase) ? 1
            : value.Equals("top", StringComparison.OrdinalIgnoreCase) ? 0
            : throw new ArgumentException($"There is no view '{value}': top or 3d");
    }

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
        if (DataContext is MainViewModel { IsConverting: true } converting)
        {
            converting.StopWork();
            return;
        }
        if (_mayClose || DataContext is not MainViewModel model || model.Map?.Session.IsModified != true)
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
