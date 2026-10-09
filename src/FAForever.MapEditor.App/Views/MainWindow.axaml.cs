using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Controls;
using FAForever.MapEditor.App.ViewModels;

namespace FAForever.MapEditor.App.Views;

public sealed partial class MainWindow : Window
{
    private bool _mayClose;
    private MainViewModel? _model;
    private readonly List<CommandAdapter> _adapters = [];

    public MainWindow()
    {
        InitializeComponent();
        Heightmap.CursorMoved += text => CursorText.Text = text;
        Terrain3D.CursorMoved += text => CursorText.Text = text;
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
    /// Which view shows: <c>top</c> or <c>3d</c>.
    /// </summary>
    public string ShownView
    {
        get => Views.SelectedIndex == 1 ? "3d" : "top";
        set => Views.SelectedIndex = value.ToLowerInvariant() switch
        {
            "top" => 0,
            "3d" => 1,
            _ => throw new ArgumentException($"There is no view '{value}': top or 3d"),
        };
    }

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
            _model.FitRequested -= OnFitRequested;
            _model.ShowFolderRequested -= OnShowFolderRequested;
        }
        _model = DataContext as MainViewModel;
        if (_model is not null)
        {
            _model.PropertyChanged += OnModelChanged;
            _model.FitRequested += OnFitRequested;
            _model.ShowFolderRequested += OnShowFolderRequested;
            BuildMenusAndShortcuts(_model);
            ShowChosenView();
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

        MainMenu.ItemsSource = Menus.Bar.Select(layout => Menu(layout, top: true)).ToList();
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
            case nameof(MainViewModel.ViewIndex):
                ShowChosenView();
                break;
            case nameof(MainViewModel.IsPaletteOpen) when _model?.IsPaletteOpen == true:
                Dispatcher.UIThread.Post(Palette.FocusQuery, DispatcherPriority.Input);
                break;
            default:
                foreach (CommandAdapter adapter in _adapters)
                {
                    adapter.Refresh();
                }
                break;
        }
    }

    private void OnFitRequested(object? sender, EventArgs e) => Heightmap.Fit();

    private async void OnShowFolderRequested(string folder) => await Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(folder));

    private void ShowChosenView()
    {
        int index = _model?.ViewIndex ?? 0;
        Heightmap.IsVisible = index == 0;
        Terrain3D.IsVisible = index == 1;
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
