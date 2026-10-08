using Avalonia.Controls;
using Avalonia.Interactivity;
using FAForever.MapEditor.App.Controls;

namespace FAForever.MapEditor.App.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Heightmap.CursorMoved += text => CursorText.Text = text;
    }

    /// <summary>
    /// The view of the map, for automation.
    /// </summary>
    public HeightmapView MapView => Heightmap;

    private void OnFit(object? sender, RoutedEventArgs e) => Heightmap.Fit();
}
