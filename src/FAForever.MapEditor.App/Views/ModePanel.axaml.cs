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
        // a units preset's context menu: put it in a slot of the chosen palette (U-8)
        AddHandler(ContextRequestedEvent, OnContextRequested, RoutingStrategies.Bubble);
        AddHandler(PointerPressedEvent, OnPresetPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnPresetDragged, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, (_, _) => _press = null, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// A right-click on a units preset of the panel's list opens a menu that puts the preset into
    /// one of the chosen palette's ten slots (<c>units.setSlot</c>); the other kinds of preset have no menu.
    /// </summary>
    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (_model is not ViewModels.MainViewModel model || e.Source is not Avalonia.Visual source
            || source.FindAncestorOfType<Button>(includeSelf: true) is not Button button
            || button.DataContext is not FAForever.MapEditor.Project.Workspaces.Preset { Kind: FAForever.MapEditor.Project.Workspaces.PresetKinds.Units } preset
            || button.ContextMenu is not null)
        {
            return;
        }
        // the button keeps the menu and opens it itself from now on; its items are made again each time it opens
        ContextMenu menu = new ContextMenu();
        menu.Opening += (_, _) => menu.ItemsSource = PaletteSlotItems(model, preset);
        menu.ItemsSource = PaletteSlotItems(model, preset);
        button.ContextMenu = menu;
        menu.Open(button);
        e.Handled = true;
    }

    /// <summary>
    /// The items of a units preset's context menu: one entry, with a slot of the chosen palette
    /// under it for each of the ten (<c>units.setSlot {slot, preset}</c>).
    /// </summary>
    private static List<object> PaletteSlotItems(ViewModels.MainViewModel model, FAForever.MapEditor.Project.Workspaces.Preset preset)
    {
        ViewModels.UnitPalettesViewModel palettes = model.UnitPalettes;
        MenuItem into = new MenuItem { Header = palettes.Chosen is string chosen ? $"Put in a slot of {chosen}" : "Put in a palette slot", IsEnabled = palettes.HasChosen };
        Avalonia.Automation.AutomationProperties.SetName(into, $"Put the preset {preset.Name} in a palette slot");
        List<object> slots = [];
        foreach (ViewModels.PaletteSlotItem slot in palettes.Slots)
        {
            MenuItem item = new MenuItem { Header = slot.IsEmpty ? $"Slot {slot.Key} (empty)" : $"Slot {slot.Key}: {slot.Title}" };
            Avalonia.Automation.AutomationProperties.SetName(item, $"Put the preset {preset.Name} in palette slot {slot.Key}");
            int number = slot.Index + 1;
            item.Click += (_, _) => _ = model.RunWithAsync("units.setSlot", new System.Text.Json.Nodes.JsonObject { ["slot"] = number, ["preset"] = preset.Name });
            slots.Add(item);
        }
        into.ItemsSource = slots;
        return [into];
    }


    // the left button pressed on a units preset of the list, until it is released or the drag starts
    private (PointerPressedEventArgs Args, Avalonia.Point At, string Preset)? _press;

    private void OnPresetPressed(object? sender, PointerPressedEventArgs e)
    {
        _press = e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.Source is Avalonia.Visual source
            && source.FindAncestorOfType<Button>(includeSelf: true)?.DataContext is FAForever.MapEditor.Project.Workspaces.Preset { Kind: FAForever.MapEditor.Project.Workspaces.PresetKinds.Units } preset
            ? (e, e.GetPosition(this), preset.Name)
            : null;
    }

    /// <summary>
    /// A units preset of the list dragged far enough goes onto a slot of the palette (U-8), which holds it.
    /// </summary>
    private async void OnPresetDragged(object? sender, PointerEventArgs e)
    {
        if (_press is not (PointerPressedEventArgs args, Avalonia.Point at, string preset))
        {
            return;
        }
        Avalonia.Vector moved = e.GetPosition(this) - at;
        if (Math.Abs(moved.X) < 6 && Math.Abs(moved.Y) < 6)
        {
            return;
        }
        _press = null;
        await DragDrop.DoDragDropAsync(args, MapPanels.UnitPalettePanel.PresetDrag(preset), DragDropEffects.Copy);
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
        WatchPresets();
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

    private ViewModels.PresetsViewModel? _presets;

    /// <summary>
    /// The preset Show the preset points at (user story U-9): its button in the panel's list of
    /// presets gets the class <c>highlighted</c> and is scrolled into view, once the list is laid
    /// out; the others lose it. Set again when the list is made again.
    /// </summary>
    private void OnPresetsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ViewModels.PresetsViewModel.Highlighted) or nameof(ViewModels.PresetsViewModel.Shown))
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(HighlightPreset, Avalonia.Threading.DispatcherPriority.Background);
        }
    }

    private void HighlightPreset()
    {
        string? highlighted = _presets?.Highlighted;
        foreach (Button button in this.GetVisualDescendants().OfType<Button>())
        {
            bool isHighlighted = highlighted is not null && button.DataContext is FAForever.MapEditor.Project.Workspaces.Preset preset
                && button.Parent is DockPanel && string.Equals(preset.Name, highlighted, StringComparison.OrdinalIgnoreCase)
                && !button.Classes.Contains("beside");
            button.Classes.Set("highlighted", isHighlighted);
            if (isHighlighted)
            {
                button.BringIntoView();
            }
        }
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        WatchPresets();
    }

    private void WatchPresets()
    {
        ViewModels.PresetsViewModel? presets = _model?.Presets;
        if (ReferenceEquals(presets, _presets))
        {
            return;
        }
        if (_presets is not null)
        {
            _presets.PropertyChanged -= OnPresetsChanged;
        }
        _presets = presets;
        if (_presets is not null)
        {
            _presets.PropertyChanged += OnPresetsChanged;
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
