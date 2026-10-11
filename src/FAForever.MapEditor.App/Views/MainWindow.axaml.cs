using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Controls;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;

namespace FAForever.MapEditor.App.Views;

public sealed partial class MainWindow : Window
{
    private bool _mayClose;
    private MainViewModel? _model;
    private MapViewTab? _shownView;
    private readonly List<CommandAdapter> _adapters = [];
    private readonly List<(KeyGesture Gesture, CommandAdapter Adapter)> _shortcuts = [];

    public MainWindow()
    {
        InitializeComponent();
        // the tool's keys work right after a tool or mode is chosen by its button, and while a control
        // that takes no typing has the keyboard (user story F-11)
        AddHandler(Button.ClickEvent, OnButtonClicked, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(KeyDownEvent, OnKeyLeftOver, Avalonia.Interactivity.RoutingStrategies.Bubble);
        // the shortcuts before the focused control, except a text field's own keys and behind a page
        AddHandler(KeyDownEvent, OnShortcutKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnPageKeyLeftOver, Avalonia.Interactivity.RoutingStrategies.Bubble);
        // a field that gives its value when it loses the keyboard gives it on Enter too
        AddHandler(KeyDownEvent, OnEnterInField, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        // a page that opens takes the keyboard; one that closes gives it back to the views
        foreach (Control page in new Control[] { DiagnosticsPageView, PluginsPageView, VersionControlPageView, ReportProblemPageView, NewMapPageView, ShortcutsPageView, WorkspaceSettingsPageView })
        {
            page.PropertyChanged += (_, e) =>
            {
                if (e.Property == IsVisibleProperty)
                {
                    Dispatcher.UIThread.Post(() => OnPageShown(page), DispatcherPriority.Loaded);
                }
            };
        }
        Heightmap.CursorMoved += text => CursorText.Text = text;
        Terrain3D.CursorMoved += text => CursorText.Text = text;
        // a splitter let go: the layout remembers the size it left its side (user story V-4)
        ModePanelSplitter.DragCompleted += (_, _) => _model?.Layout.ResizeSide(PanelPlace.Left, Shell.ColumnDefinitions[0].ActualWidth);
        InspectorSplitter.DragCompleted += (_, _) => _model?.Layout.ResizeSide(PanelPlace.Right, Shell.ColumnDefinitions[4].ActualWidth);
        DockSplitter.DragCompleted += (_, _) => _model?.Layout.ResizeSide(PanelPlace.Bottom, Middle.RowDefinitions[2].ActualHeight);
        ViewsSplitter.DragCompleted += (_, _) =>
        {
            double top = Maps.ColumnDefinitions[0].ActualWidth;
            double both = top + Maps.ColumnDefinitions[2].ActualWidth;
            if (both > 0)
            {
                _model?.Layout.Split(top / both);
            }
        };
    }

    /// <summary>
    /// The view of the map from above, for automation.
    /// </summary>
    public HeightmapView MapView => Heightmap;

    /// <summary>
    /// The view of the map in 3D, on the graphics card, for automation.
    /// </summary>
    public TerrainViewGpu View3D => Terrain3D;

    /// <summary>
    /// Closes the window without asking about unsaved changes, for automation.
    /// </summary>
    public void CloseWithoutAsking()
    {
        _mayClose = true;
        Close();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_model is not null)
        {
            _model.PropertyChanged -= OnModelChanged;
            _model.Commands.CanRunChanged -= OnCanRunChanged;
            _model.Commands.CommandsChanged -= OnCommandsChanged;
            _model.Palette.PropertyChanged -= OnPaletteChanged;
            _model.FitRequested -= OnFitRequested;
            _model.TopViewRequested -= OnTopViewRequested;
            _model.TopViewFrameRequested -= OnTopViewFrameRequested;
            _model.ShowFolderRequested -= OnShowFolderRequested;
            _model.OpenFileRequested -= OnOpenFileRequested;
            _model.Layout.PropertyChanged -= OnLayoutChanged;
            _model.Layout.Changed -= OnLayoutResized;
            _model.Layout.PlacesChanged -= OnPlacesChanged;
        }
        _model = DataContext as MainViewModel;
        if (_model is not null)
        {
            _model.PropertyChanged += OnModelChanged;
            _model.Commands.CanRunChanged += OnCanRunChanged;
            _model.Commands.CommandsChanged += OnCommandsChanged;
            _model.Palette.PropertyChanged += OnPaletteChanged;
            _model.FitRequested += OnFitRequested;
            _model.TopViewRequested += OnTopViewRequested;
            _model.TopViewFrameRequested += OnTopViewFrameRequested;
            _model.ShowFolderRequested += OnShowFolderRequested;
            _model.OpenFileRequested += OnOpenFileRequested;
            _model.Layout.PropertyChanged += OnLayoutChanged;
            _model.Layout.Changed += OnLayoutResized;
            _model.Layout.PlacesChanged += OnPlacesChanged;
            BuildMenusAndShortcuts(_model);
            BuildOverlayChips(_model);
            ShowChosenView();
            ArrangePanels();
        }
    }

    /// <summary>
    /// A chip per overlay (<see cref="MainViewModel.Overlays"/>) in the views' row after the symmetry,
    /// in their order: a toggle that runs the overlay's command, checked while it shows, with a
    /// swatch of the colour it lays over the terrain. What does not fit goes behind the row's More.
    /// </summary>
    private void BuildOverlayChips(MainViewModel model)
    {
        foreach (Control chip in ViewOptions.Children.Where(child => child.Classes.Contains("chip") && child.DataContext is OverlayItem).ToList())
        {
            ViewOptions.Children.Remove(chip);
        }
        Avalonia.Automation.AutomationProperties.SetName(ViewOptions.MoreButton, "More of what the views show");
        foreach (OverlayItem overlay in model.Overlays)
        {
            Avalonia.Controls.Primitives.ToggleButton chip = new Avalonia.Controls.Primitives.ToggleButton
            {
                DataContext = overlay,
                Command = model.RunCommand,
                CommandParameter = overlay.Definition.CommandId,
            };
            chip.Classes.Add("chip");
            chip.Classes.Set("planned", overlay.IsIncomplete);
            chip.Bind(Avalonia.Controls.Primitives.ToggleButton.IsCheckedProperty, new Avalonia.Data.Binding(nameof(OverlayItem.IsOn)) { Mode = Avalonia.Data.BindingMode.OneWay });
            Avalonia.Automation.AutomationProperties.SetName(chip, $"Show {overlay.Title}");
            if (overlay.Definition.Missing is string missing)
            {
                Avalonia.Automation.AutomationProperties.SetHelpText(chip, $"Not built yet: {missing}");
            }
            TextBlock title = new TextBlock();
            // its title, and a setting of it: the grid's spacing
            title.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(nameof(OverlayItem.Text)) { Mode = Avalonia.Data.BindingMode.OneWay });
            if (overlay.Id == "grid")
            {
                // the grid's spacing: another one from the chip's context menu (view.gridSpacing)
                chip.ContextMenu = new ContextMenu
                {
                    ItemsSource = MainViewModel.GridSpacings.Select(spacing => new MenuItem
                    {
                        Header = $"Every {spacing} {(spacing == 1 ? "ogrid" : "ogrids")}",
                        Command = model.ChooseGridSpacingCommand,
                        CommandParameter = spacing,
                    }).ToList(),
                };
                ToolTip.SetTip(chip, "The grid's lines over the terrain; right-click for another spacing");
                Avalonia.Automation.AutomationProperties.SetHelpText(chip, "Right-click for another spacing, or run view.gridSpacing");
            }
            if (overlay.Definition.Swatch is string swatch)
            {
                Border legend = new Border();
                legend.Classes.Add("swatch");
                legend.Bind(Border.BackgroundProperty, legend.GetResourceObservable(swatch));
                ToolTip.SetTip(chip, $"{overlay.Title}: shown in this colour over the terrain");
                StackPanel content = new StackPanel();
                content.Classes.Add("inline");
                content.Children.Add(legend);
                content.Children.Add(title);
                chip.Content = content;
            }
            else
            {
                chip.Content = title;
            }
            ViewOptions.Children.Add(chip);
        }
    }

    /// <summary>
    /// The menu bar (<see cref="Menus.Bar"/>) and the window's shortcuts, from the commands: one
    /// adapter per command, used by its menu item and its shortcut.
    /// </summary>
    private void BuildMenusAndShortcuts(MainViewModel model)
    {
        _adapters.Clear();
        _shortcuts.Clear();
        Dictionary<string, CommandAdapter> adapters = [];
        foreach (EditorCommand command in model.Commands.All)
        {
            CommandAdapter adapter = new CommandAdapter(model, command);
            adapters[command.Id] = adapter;
            _adapters.Add(adapter);
            if (command.Gesture is string gesture)
            {
                _shortcuts.Add((KeyGesture.Parse(gesture), adapter));
            }
        }
        _shortcuts.Add((KeyGesture.Parse("Ctrl+Shift+Z"), adapters["edit.redo"]));

        MenuItem Menu(MenuLayout layout, bool top)
        {
            MenuItem menu = new MenuItem { Header = top ? "_" + layout.Title : layout.Title };
            // a mode's submenu shows the mode's key (F2) beside its name, as its first item does
            if (!top && layout.Items.FirstOrDefault() is string first && first.EndsWith(".mode", StringComparison.Ordinal)
                && adapters[first].Command.Gesture is string modeKey)
            {
                menu.InputGesture = KeyGesture.Parse(modeKey);
            }
            foreach (object entry in layout.Items)
            {
                menu.Items.Add(entry switch
                {
                    MenuLayout submenu => Menu(submenu, top: false),
                    Menus.Separator => new Separator(),
                    string id => Item(adapters[id]),
                    _ => throw new InvalidOperationException($"The menu {layout.Title} holds {entry}"),
                });
            }
            return menu;
        }

        MainMenu.ItemsSource = Menus.WithPlugins(model.Commands.All).Select(layout => Menu(layout, top: true)).ToList();
        OnCanRunChanged(this, EventArgs.Empty);
    }

    // a plugin registered or took away its commands: the menus and shortcuts are made again
    private void OnCommandsChanged(object? sender, EventArgs e)
    {
        if (_model is not null)
        {
            BuildMenusAndShortcuts(_model);
        }
    }

    private static MenuItem Item(CommandAdapter adapter)
    {
        EditorCommand command = adapter.Command;
        MenuItem item = new MenuItem { Header = command.Title, Command = adapter };
        if (command.Incomplete is IncompleteAttribute incomplete)
        {
            item.Classes.Add("planned");
            ToolTip.SetTip(item, $"Not built yet: user story {incomplete.Story}. {incomplete.Missing}");
        }
        if (command.Gesture is string gesture)
        {
            item.InputGesture = KeyGesture.Parse(gesture);
        }
        return item;
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.View):
                ShowChosenView();
                break;
            case nameof(MainViewModel.GpuDebug):
                Terrain3D.Debug = _model!.GpuDebug;
                break;
            case nameof(MainViewModel.IsDockTall):
                ApplyLayout();
                break;
            case nameof(MainViewModel.Dock):
                // a tab chosen that lies outside the dock: its window comes to the front instead
                if (_model!.Layout.PlaceOf(LayoutViewModel.PanelOf(_model.Dock)) != PanelPlace.Dock)
                {
                    ActivateFloating(LayoutViewModel.PanelOf(_model.Dock));
                    Dispatcher.UIThread.Post(ArrangePanels);
                }
                break;
        }
    }

    // a panel shown or hidden puts the panels in their places again; a size only lays out again
    private void OnLayoutChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LayoutViewModel.ShowModePanel) or nameof(LayoutViewModel.ShowInspector) or nameof(LayoutViewModel.ShowDock))
        {
            ArrangePanels();
        }
        else
        {
            ApplyLayout();
        }
    }

    private void OnLayoutResized(object? sender, EventArgs e)
    {
        ApplyLayout();
        ShowChosenView();
    }

    /// <summary>
    /// Lays the sides out as the layout says (user story V-4): a side that holds a panel that shows
    /// at its size (the editor's own from the tokens when the user gave none), an empty one
    /// collapsed with its splitter (<see cref="ArrangePanels"/> put the panels there). The bottom is
    /// higher while the dock lies there showing the asset shelf, unless the user gave it a height.
    /// </summary>
    private void ApplyLayout()
    {
        if (_model is null)
        {
            return;
        }
        LayoutViewModel layout = _model.Layout;
        bool left = LeftSide.IsVisible, right = RightSide.IsVisible, bottom = BottomSide.IsVisible;
        ColumnDefinition modePanel = Shell.ColumnDefinitions[0];
        modePanel.Width = left ? new GridLength(layout.ModePanelWidth ?? Token("ModePanelWidth")) : GridLength.Auto;
        modePanel.MinWidth = left ? Token("LayoutPanelMinWidth") : 0;
        ColumnDefinition inspector = Shell.ColumnDefinitions[4];
        inspector.Width = right ? new GridLength(layout.InspectorWidth ?? Token("InspectorWidth")) : GridLength.Auto;
        inspector.MinWidth = right ? Token("LayoutPanelMinWidth") : 0;
        Shell.ColumnDefinitions[2].MinWidth = Token("LayoutViewMinWidth");
        RowDefinition dock = Middle.RowDefinitions[2];
        bool tall = _model.IsDockTall && layout.PlaceOf(MovablePanel.Dock) == PanelPlace.Bottom;
        dock.Height = bottom ? new GridLength(layout.DockHeight ?? Token(tall ? "DockTallHeight" : "DockHeight")) : GridLength.Auto;
        dock.MinHeight = bottom ? Token("LayoutDockMinHeight") : 0;
        Middle.RowDefinitions[0].MinHeight = Token("LayoutDockMinHeight");
    }

    /// <summary>
    /// A size of <c>Styles/Tokens.axaml</c>.
    /// </summary>
    private double Token(string key) => this.TryFindResource(key, ActualThemeVariant, out object? value) && value is double size ? size : 0;

    // the menu items and shortcuts ask the registry again whether their command can run
    private void OnCanRunChanged(object? sender, EventArgs e)
    {
        using Performance.Span measure = Performance.Measure("window.commands");
        foreach (CommandAdapter adapter in _adapters)
        {
            adapter.Refresh();
        }
    }

    private void OnPaletteChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PaletteViewModel.IsOpen) && _model?.Palette.IsOpen == true)
        {
            Dispatcher.UIThread.Post(Palette.FocusQuery, DispatcherPriority.Input);
        }
    }

    /// <summary>
    /// A tool's or mode's button clicked: the view that shows takes the keyboard, so the tool's
    /// keys (Q, E, the arrows, Escape, +, S) work at once, as after a click in the view.
    /// </summary>
    private void OnButtonClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (e.Source is Button button && (button.Classes.Contains("tool") || button.Classes.Contains("rail")))
        {
            Dispatcher.UIThread.Post(FocusShownView, DispatcherPriority.Input);
        }
    }

    /// <summary>
    /// Gives the keyboard to the view that shows (the top view when both do).
    /// </summary>
    public void FocusShownView()
    {
        if (Heightmap.IsEffectivelyVisible)
        {
            Heightmap.Focus();
        }
        else if (Terrain3D.IsEffectivelyVisible)
        {
            Terrain3D.Focus();
        }
    }

    /// <summary>
    /// A key nothing took, while the keyboard is in the editor's panels on a control that takes no
    /// typing (a button, a check box) or nowhere: it goes to the active tool, as in the views. Not
    /// while a page or question shows over the editor.
    /// </summary>
    private void OnKeyLeftOver(object? sender, KeyEventArgs e)
    {
        if (e.Handled || _model is not { HasMap: true } model
            || model.Question.IsOpen || model.Palette.IsOpen || model.Picker.IsOpen || model.IsPageOpen)
        {
            return;
        }
        object? focused = FocusManager?.GetFocusedElement();
        if (focused is TextBox or ComboBox or HeightmapView or TerrainViewGpu
            || focused is Avalonia.Visual visual && !Shell.IsVisualAncestorOf(visual))
        {
            return;
        }
        if (model.ToolKey(e.Key.ToString(), HeightmapView.ModifiersOf(e.KeyModifiers)))
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// The window's shortcuts (the commands' gestures), before the focused control sees the key:
    /// not while a question, the palette or the picker shows, nor behind a page (whose keys stay
    /// in it: a key that reaches the window from behind it gives the page the keyboard, Escape
    /// closes it), and never a text field's own keys (Ctrl+A selects its text, Ctrl+Z undoes its
    /// typing, Delete deletes a letter).
    /// </summary>
    private void OnShortcutKey(object? sender, KeyEventArgs e)
    {
        if (e.Handled || _model is not MainViewModel model || model.Question.IsOpen)
        {
            return;
        }
        object? focused = FocusManager?.GetFocusedElement();
        if (model.IsPageOpen)
        {
            if (OpenPage is Control page && !(focused is Avalonia.Visual inside && page.IsVisualAncestorOf(inside)))
            {
                // the keyboard was behind the page (it opened with a shortcut): the key goes no further
                if (e.Key == Key.Escape)
                {
                    model.ClosePage();
                    Dispatcher.UIThread.Post(FocusShownView, DispatcherPriority.Input);
                }
                else
                {
                    FocusFirstIn(page);
                }
                e.Handled = true;
            }
            return;
        }
        if (model.Palette.IsOpen || model.Picker.IsOpen)
        {
            return;
        }
        if (focused is TextBox && IsTextKey(e.Key, e.KeyModifiers))
        {
            return;
        }
        foreach ((KeyGesture gesture, CommandAdapter adapter) in _shortcuts)
        {
            if (gesture.Matches(e) && adapter.CanExecute(null))
            {
                adapter.Execute(null);
                e.Handled = true;
                return;
            }
        }
    }

    /// <summary>
    /// Whether a text field takes the key itself: without control or alt every key types or moves
    /// the caret, and with control its own editing keys (select all, copy, cut, paste, undo, redo,
    /// a word left or right or deleted).
    /// </summary>
    internal static bool IsTextKey(Key key, KeyModifiers modifiers) =>
        (modifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) == 0 && key is not (>= Key.F1 and <= Key.F24)
        || (modifiers & (KeyModifiers.Alt | KeyModifiers.Meta)) == 0 && key is Key.A or Key.C or Key.X or Key.V or Key.Z or Key.Y
            or Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.Back or Key.Delete or Key.Insert;

    /// <summary>
    /// Enter in a one-line text field gives its text to what it is bound to, as losing the keyboard
    /// does (the options row's numbers, the inspector's fields bound on losing the keyboard); the
    /// key goes on to what else Enter does there.
    /// </summary>
    private void OnEnterInField(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && e.Source is TextBox { AcceptsReturn: false } field)
        {
            Avalonia.Data.BindingOperations.GetBindingExpressionBase(field, TextBox.TextProperty)?.UpdateSource();
        }
    }

    /// <summary>
    /// Escape that nothing on the page took (a combo box closes its list first) closes the page.
    /// </summary>
    private void OnPageKeyLeftOver(object? sender, KeyEventArgs e)
    {
        if (!e.Handled && e.Key == Key.Escape && _model is { IsPageOpen: true, Question.IsOpen: false } model && model.ClosePage())
        {
            e.Handled = true;
            Dispatcher.UIThread.Post(FocusShownView, DispatcherPriority.Input);
        }
    }

    /// <summary>
    /// The page that shows over the editor, the one on top when several do.
    /// </summary>
    private Control? OpenPage => _model?.OpenPageName switch
    {
        "workspaceSettings" => WorkspaceSettingsPageView,
        "shortcuts" => ShortcutsPageView,
        "newMap" => NewMapPageView,
        "report" => ReportProblemPageView,
        "versionControl" => VersionControlPageView,
        "plugins" => PluginsPageView,
        "diagnostics" => DiagnosticsPageView,
        _ => null,
    };

    private void OnPageShown(Control page)
    {
        object? focused = FocusManager?.GetFocusedElement();
        bool inside = focused is Avalonia.Visual visual && page.IsVisualAncestorOf(visual);
        if (page.IsVisible && OpenPage == page && !inside)
        {
            FocusFirstIn(page);
        }
        else if (!page.IsVisible && (inside || focused is null) && _model?.IsPageOpen == false)
        {
            FocusShownView();
        }
    }

    private static void FocusFirstIn(Control page)
    {
        if (page.GetVisualDescendants().OfType<InputElement>().FirstOrDefault(element => element.Focusable && element.IsEffectivelyVisible && element.IsEffectivelyEnabled
            && element is TextBox or Button or ComboBox or CheckBox or ListBox or Slider or TreeView) is InputElement first)
        {
            first.Focus(NavigationMethod.Tab);
        }
    }

    private void OnFitRequested(object? sender, EventArgs e) => Heightmap.Fit();

    private void OnTopViewFrameRequested(double x0, double z0, double x1, double z1) => Heightmap.ShowArea(x0, z0, x1, z1);

    private void OnTopViewRequested(double? x, double? z, double? zoom)
    {
        Avalonia.Point centre = Heightmap.Centre;
        Heightmap.Show(zoom ?? Heightmap.Zoom, x ?? centre.X, z ?? centre.Y);
    }

    private async void OnShowFolderRequested(string folder) => await Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(folder));

    /// <summary>
    /// Opens a file in the system's application for it, such as the map's script (user story M-6).
    /// </summary>
    private async void OnOpenFileRequested(string file) => await Launcher.LaunchFileInfoAsync(new FileInfo(file));

    /// <summary>
    /// Shows the chosen view, or both side by side (user story V-4): the views only change their
    /// visibility and column, so both stay in the tree and the 3D view keeps its context.
    /// </summary>
    private void ShowChosenView()
    {
        MapViewTab view = _model?.View ?? MapViewTab.Top;
        bool sideBySide = view == MapViewTab.SideBySide;
        // the top view's width changes by half or double: it shows the whole map again, once laid out
        if (_shownView is MapViewTab shown && shown != view && (shown == MapViewTab.SideBySide || view == MapViewTab.SideBySide) && view != MapViewTab.ThreeD)
        {
            Dispatcher.UIThread.Post(Heightmap.Fit, DispatcherPriority.Loaded);
        }
        _shownView = view;
        Heightmap.IsVisible = view is MapViewTab.Top or MapViewTab.SideBySide;
        Terrain3D.IsVisible = view is MapViewTab.ThreeD or MapViewTab.SideBySide;
        ViewsSplitter.IsVisible = sideBySide;
        Grid.SetColumnSpan(Heightmap, sideBySide ? 1 : 3);
        Grid.SetColumn(Terrain3D, sideBySide ? 2 : 0);
        Grid.SetColumnSpan(Terrain3D, sideBySide ? 1 : 3);
        double split = _model?.Layout.SideBySideSplit ?? 0.5;
        Maps.ColumnDefinitions[0].Width = new GridLength(split, GridUnitType.Star);
        Maps.ColumnDefinitions[2].Width = new GridLength(1 - split, GridUnitType.Star);
        Maps.ColumnDefinitions[0].MinWidth = sideBySide ? Token("LayoutViewMinWidth") : 0;
        Maps.ColumnDefinitions[2].MinWidth = sideBySide ? Token("LayoutViewMinWidth") : 0;
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (DataContext is MainViewModel { Setup.IsConverting: true } converting)
        {
            converting.Setup.StopWork();
            return;
        }
        if (_mayClose || DataContext is not MainViewModel model || model.Map?.Session.IsModified != true)
        {
            // the floating panels' windows close with the editor's, and float again at the next start
            CloseFloatingWindows();
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

    /// <summary>
    /// A command as a menu item's or shortcut's command: it runs through the registry, so a command
    /// not built yet reports what it is to do.
    /// </summary>
    private sealed class CommandAdapter(MainViewModel model, EditorCommand command) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public EditorCommand Command => command;

        public bool CanExecute(object? parameter) => command.IsIncomplete || model.Commands.CanRun(command);

        public void Execute(object? parameter)
        {
            // a shortcut while a question shows over the window waits for its answer
            if (!model.Question.IsOpen)
            {
                _ = model.RunAsync(command.Id);
            }
        }

        public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
