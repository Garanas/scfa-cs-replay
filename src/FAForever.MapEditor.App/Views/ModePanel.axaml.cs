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
    /// group): the inspector shows that part of the mode, over a tile chosen on the shelf and over
    /// the selection, which stays (<see cref="ViewModels.MainViewModel.InspectTheMode"/>). A unit of
    /// the Units mode's tree is selected instead, and the selection has the inspector.
    /// </summary>
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_model is null || e.Source is not Avalonia.Visual source)
        {
            return;
        }
        TreeViewItem? node = source.FindAncestorOfType<TreeViewItem>(includeSelf: true);
        if (node?.DataContext is ViewModels.UnitNode unit)
        {
            _model.InspectUnit(unit);
        }
        else if (node is not null || source.FindAncestorOfType<ListBoxItem>(includeSelf: true) is not null)
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
        else if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && e.Source is TextBox name && name.Classes.Contains("presetname")
            && _model?.Presets.SaveNewCommand is { } save && save.CanExecute(null) && _model.Presets.HasLibrary)
        {
            // Enter in a new preset's name presses Save
            BindingOperations.GetBindingExpressionBase(name, TextBox.TextProperty)?.UpdateSource();
            save.Execute(null);
            e.Handled = true;
        }
    }
}
