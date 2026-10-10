using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FAForever.MapEditor.App.ViewModels;

namespace FAForever.MapEditor.App.Views;

/// <summary>
/// The picker: the search has the keyboard when it opens; the arrow keys move through the tiles
/// (up and down from the search, all four in the tiles), Enter or a double click chooses, Escape
/// or a click beside it cancels. It tells the view model how many tiles fit in a row.
/// </summary>
public sealed partial class AssetPicker : UserControl
{
    /// <summary>
    /// The room a row loses to the list's padding and its scroll bar, in pixels.
    /// </summary>
    private const double RowMargin = 40;

    private AssetPickerViewModel? _model;

    public AssetPicker()
    {
        InitializeComponent();
        Grid.SizeChanged += (_, _) => MeasureColumns();
        Grid.AddHandler(KeyDownEvent, OnGridKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnEscape, RoutingStrategies.Tunnel);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_model is not null)
        {
            _model.Opened -= OnOpened;
            _model.PropertyChanged -= OnModelChanged;
        }
        _model = DataContext as AssetPickerViewModel;
        if (_model is not null)
        {
            _model.Opened += OnOpened;
            _model.PropertyChanged += OnModelChanged;
            MeasureColumns();
        }
    }

    /// <summary>
    /// Puts the keyboard in the search field.
    /// </summary>
    public void FocusSearch() => Search.Focus();

    // the picker shows first, then the field can take the keyboard
    private void OnOpened(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            Search.Focus();
            MeasureColumns();
            ShowSelected(focus: false);
        }, DispatcherPriority.Input);

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AssetPickerViewModel.Selected) && _model?.Selected is not null)
        {
            Dispatcher.UIThread.Post(() => ShowSelected(focus: false), DispatcherPriority.Background);
        }
    }

    private void MeasureColumns()
    {
        if (_model is not null && Grid.Bounds.Width > 0)
        {
            double tile = _model.TileWidth + (double)(this.FindResource("TileGap") is Thickness gap ? gap.Right : 0);
            _model.Columns = Math.Max(1, (int)((Grid.Bounds.Width - RowMargin) / tile));
        }
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (_model is null)
        {
            return;
        }
        switch (e.Key)
        {
            case Key.Down or Key.Up:
                _model.Move(0, e.Key == Key.Down ? 1 : -1);
                ShowSelected(focus: false);
                e.Handled = true;
                break;
            case Key.Enter:
                _model.Choose();
                e.Handled = true;
                break;
        }
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
        else if (e.Key == Key.Enter)
        {
            _model.Choose();
            e.Handled = true;
        }
    }

    private void OnEscape(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _model is not null)
        {
            _model.Cancel();
            e.Handled = true;
        }
    }

    private void OnTileDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_model is not null && (sender as Control)?.DataContext is AssetTile tile)
        {
            _model.Choose(tile);
            e.Handled = true;
        }
    }

    private void OnScrimPressed(object? sender, PointerPressedEventArgs e) => _model?.Cancel();

    // a click on the picker itself keeps it open
    private void OnDialogPressed(object? sender, PointerPressedEventArgs e) => e.Handled = true;

    /// <summary>
    /// Scrolls the selected tile's row into sight, and gives that tile the keyboard when asked.
    /// </summary>
    private void ShowSelected(bool focus)
    {
        if (_model?.Selected is not AssetTile tile || _model.SelectedIndex is not (>= 0 and int index))
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
