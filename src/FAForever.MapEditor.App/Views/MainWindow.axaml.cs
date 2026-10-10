using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Controls;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.ViewModels;

namespace FAForever.MapEditor.App.Views;

public sealed partial class MainWindow : Window
{
    private bool _mayClose;
    private MainViewModel? _model;
    private MapViewTab? _shownView;
    private readonly List<CommandAdapter> _adapters = [];

    public MainWindow()
    {
        InitializeComponent();
        Heightmap.CursorMoved += text => CursorText.Text = text;
        Terrain3D.CursorMoved += text => CursorText.Text = text;
        // a splitter let go: the layout remembers the size it left (user story V-4)
        ModePanelSplitter.DragCompleted += (_, _) => _model?.Layout.Resize(LayoutPanel.ModePanel, Shell.ColumnDefinitions[0].ActualWidth);
        InspectorSplitter.DragCompleted += (_, _) => _model?.Layout.Resize(LayoutPanel.Inspector, Shell.ColumnDefinitions[4].ActualWidth);
        DockSplitter.DragCompleted += (_, _) => _model?.Layout.Resize(LayoutPanel.Dock, Middle.RowDefinitions[2].ActualHeight);
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
            BuildMenusAndShortcuts(_model);
            ShowChosenView();
            ApplyLayout();
        }
    }

    /// <summary>
    /// The menu bar (<see cref="Menus.Bar"/>) and the window's shortcuts, from the commands: one
    /// adapter per command, used by its menu item and its shortcut.
    /// </summary>
    private void BuildMenusAndShortcuts(MainViewModel model)
    {
        _adapters.Clear();
        KeyBindings.Clear();
        Dictionary<string, CommandAdapter> adapters = [];
        foreach (EditorCommand command in model.Commands.All)
        {
            CommandAdapter adapter = new CommandAdapter(model, command);
            adapters[command.Id] = adapter;
            _adapters.Add(adapter);
            if (command.Gesture is string gesture)
            {
                KeyBindings.Add(new KeyBinding { Gesture = KeyGesture.Parse(gesture), Command = adapter });
            }
        }
        KeyBindings.Add(new KeyBinding { Gesture = KeyGesture.Parse("Ctrl+Shift+Z"), Command = adapters["edit.redo"] });

        MenuItem Menu(MenuLayout layout, bool top)
        {
            MenuItem menu = new MenuItem { Header = top ? "_" + layout.Title : layout.Title };
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
        }
    }

    private void OnLayoutChanged(object? sender, PropertyChangedEventArgs e) => ApplyLayout();

    private void OnLayoutResized(object? sender, EventArgs e)
    {
        ApplyLayout();
        ShowChosenView();
    }

    /// <summary>
    /// Lays the panels out as the layout says (user story V-4): a shown panel's column or row at
    /// its size (the editor's own from the tokens when the user gave none), a hidden one's
    /// collapsed with its splitter. The dock is higher while the asset shelf shows, unless the
    /// user gave it a height.
    /// </summary>
    private void ApplyLayout()
    {
        if (_model is null)
        {
            return;
        }
        LayoutViewModel layout = _model.Layout;
        ColumnDefinition modePanel = Shell.ColumnDefinitions[0];
        modePanel.Width = layout.ShowModePanel ? new GridLength(layout.ModePanelWidth ?? Token("ModePanelWidth")) : GridLength.Auto;
        modePanel.MinWidth = layout.ShowModePanel ? Token("LayoutPanelMinWidth") : 0;
        ColumnDefinition inspector = Shell.ColumnDefinitions[4];
        inspector.Width = layout.ShowInspector ? new GridLength(layout.InspectorWidth ?? Token("InspectorWidth")) : GridLength.Auto;
        inspector.MinWidth = layout.ShowInspector ? Token("LayoutPanelMinWidth") : 0;
        Shell.ColumnDefinitions[2].MinWidth = Token("LayoutViewMinWidth");
        RowDefinition dock = Middle.RowDefinitions[2];
        dock.Height = layout.ShowDock ? new GridLength(layout.DockHeight ?? Token(_model.IsDockTall ? "DockTallHeight" : "DockHeight")) : GridLength.Auto;
        dock.MinHeight = layout.ShowDock ? Token("LayoutDockMinHeight") : 0;
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

        public void Execute(object? parameter) => _ = model.RunAsync(command.Id);

        public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
