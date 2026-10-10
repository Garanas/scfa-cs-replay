using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FAForever.MapEditor.App.Controls;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Views;

/// <summary>
/// The asset shelf: it tells the view model how many tiles fit in a row, and takes the arrow keys,
/// Escape, and control with the wheel (the tile size) for the tiles.
/// </summary>
public sealed partial class AssetShelf : UserControl
{
    /// <summary>
    /// The room a row loses to the list's padding and its scroll bar, in pixels.
    /// </summary>
    private const double RowMargin = 40;

    private AssetsViewModel? _model;

    public AssetShelf()
    {
        InitializeComponent();
        Grid.SizeChanged += (_, _) => MeasureColumns();
        Grid.AddHandler(KeyDownEvent, OnGridKeyDown, RoutingStrategies.Tunnel);
        Grid.AddHandler(PointerWheelChangedEvent, OnGridWheel, RoutingStrategies.Tunnel);
        Grid.AddHandler(PointerPressedEvent, OnTilePressed, RoutingStrategies.Tunnel);
        Grid.AddHandler(PointerMovedEvent, OnTileDragged, RoutingStrategies.Tunnel);
        Grid.AddHandler(PointerReleasedEvent, (_, _) => _press = null, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// How far the mouse moves with the button down before a tile is dragged, in pixels.
    /// </summary>
    private const double DragDistance = 6;

    // the left button pressed on a tile, until it is released or the drag starts
    private (PointerPressedEventArgs Args, Point At, AssetTile Tile)? _press;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_model is not null)
        {
            _model.FocusRequested -= OnFocusRequested;
            _model.PropertyChanged -= OnModelChanged;
        }
        _model = DataContext as AssetsViewModel;
        if (_model is not null)
        {
            _model.FocusRequested += OnFocusRequested;
            _model.PropertyChanged += OnModelChanged;
            MeasureColumns();
        }
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AssetsViewModel.TileWidth))
        {
            MeasureColumns();
        }
        else if (e.PropertyName == nameof(AssetsViewModel.Selected) && _model?.Selected is not null)
        {
            Dispatcher.UIThread.Post(() => ShowSelected(focus: false), DispatcherPriority.Background);
        }
    }

    private void MeasureColumns()
    {
        if (_model is not null && Grid.Bounds.Width > 0)
        {
            double tile = _model.TileWidth + (double)(this.FindResource("TileGap") is Avalonia.Thickness gap ? gap.Right : 0);
            _model.Columns = Math.Max(1, (int)((Grid.Bounds.Width - RowMargin) / tile));
        }
    }

    // the tab shows first, then the field can take the keyboard
    private void OnFocusRequested(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() => Search.Focus(), DispatcherPriority.Background);

    private void OnGridWheel(object? sender, PointerWheelEventArgs e)
    {
        if (_model is not null && e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Delta.Y != 0)
        {
            _model.TileSize = Math.Clamp(_model.TileSize + Math.Sign(e.Delta.Y), 0, 2);
            e.Handled = true;
        }
    }

    private void OnTilePressed(object? sender, PointerPressedEventArgs e)
    {
        _press = e.GetCurrentPoint(Grid).Properties.IsLeftButtonPressed
            && (e.Source as Visual)?.FindAncestorOfType<Button>(includeSelf: true)?.DataContext is AssetTile tile
            ? (e, e.GetPosition(Grid), tile)
            : null;
    }

    // a tile dragged far enough goes onto a view of the map (AssetDrag), which places it
    private async void OnTileDragged(object? sender, PointerEventArgs e)
    {
        if (_press is not (PointerPressedEventArgs args, Point at, AssetTile tile))
        {
            return;
        }
        Vector moved = e.GetPosition(Grid) - at;
        if (Math.Abs(moved.X) < DragDistance && Math.Abs(moved.Y) < DragDistance)
        {
            return;
        }
        _press = null;
        // of the selected family, the variant the inspector shows
        AssetEntry entry = _model?.Selected == tile && _model.SelectedMember is AssetEntry member ? member : tile.Entry;
        await DragDrop.DoDragDropAsync(args, AssetDrag.Of(entry), DragDropEffects.Copy);
    }

    private void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (_model is null)
        {
            return;
        }
        (int across, int down)? move = e.Key switch
        {
            Key.Left => (-1, 0),
            Key.Right => (1, 0),
            Key.Up => (0, -1),
            Key.Down => (0, 1),
            _ => null,
        };
        if (move is (int x, int y))
        {
            _model.Move(x, y);
            ShowSelected(focus: true);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _model.HasSelection)
        {
            _model.ClearSelectionCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Scrolls the selected tile's row into sight, and gives that tile the keyboard.
    /// </summary>
    private void ShowSelected(bool focus)
    {
        if (_model?.Selected is not AssetTile tile)
        {
            return;
        }
        int index = 0;
        foreach (AssetTile shown in _model.Tiles)
        {
            if (shown == tile)
            {
                break;
            }
            index++;
        }
        if (index >= _model.Tiles.Count)
        {
            return;
        }
        Grid.ScrollIntoView(index / Math.Max(1, _model.Columns));
        if (!focus)
        {
            return;
        }
        Dispatcher.UIThread.Post(() =>
        {
            foreach (Button button in Grid.GetVisualDescendants().OfType<Button>())
            {
                if (button.DataContext == tile)
                {
                    button.Focus(NavigationMethod.Directional);
                    break;
                }
            }
        }, DispatcherPriority.Background);
    }
}
