using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace FAForever.MapEditor.App.Views;

public sealed partial class ModePanel : UserControl
{
    public ModePanel()
    {
        InitializeComponent();
        // a field of class "commit" gives its value when it loses the keyboard, and on Enter
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        // an entry of the panel's lists chosen gives the inspector back to the mode
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    /// <summary>
    /// A press on an item of a list or tree of the panel (a stratum, a height layer, a mask, a
    /// group): the inspector shows the mode again, over a tile chosen on the shelf
    /// (<see cref="ViewModels.MainViewModel.InspectTheMode"/>).
    /// </summary>
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_model is not null && e.Source is Avalonia.Visual source
            && (source.FindAncestorOfType<ListBoxItem>(includeSelf: true) is not null || source.FindAncestorOfType<TreeViewItem>(includeSelf: true) is not null))
        {
            _model.InspectTheMode();
        }
    }

    private ViewModels.MainViewModel? _model;

    /// <summary>
    /// Another mode: its panel opens at its top, with its name and tools in sight.
    /// </summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_model is not null)
        {
            _model.PropertyChanged -= OnModelChanged;
        }
        _model = DataContext as ViewModels.MainViewModel;
        if (_model is not null)
        {
            _model.PropertyChanged += OnModelChanged;
        }
    }

    private void OnModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModels.MainViewModel.SelectedMode))
        {
            Scroller.Offset = default;
            // again once the new mode's sections are laid out: the scroll viewer keeps what it showed in sight otherwise
            Avalonia.Threading.Dispatcher.UIThread.Post(() => Scroller.Offset = default, Avalonia.Threading.DispatcherPriority.Background);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.Source is TextBox field && field.Classes.Contains("commit"))
        {
            BindingOperations.GetBindingExpressionBase(field, TextBox.TextProperty)?.UpdateSource();
            e.Handled = true;
        }
    }
}
