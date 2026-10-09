using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Controls;
using FAForever.MapEditor.App.ViewModels;

namespace FAForever.MapEditor.App.Views;

public sealed partial class MainWindow : Window
{
    /// <summary>
    /// The menus of the menu bar, in order; the commands of the modes go under Modes, by mode.
    /// </summary>
    private static readonly string[] MenuBar = ["File", "Edit", "Select", "View", "Map", "Modes", "Generate", "Plugins"];

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
        }
        _model = DataContext as MainViewModel;
        if (_model is not null)
        {
            _model.PropertyChanged += OnModelChanged;
            _model.FitRequested += OnFitRequested;
            BuildMenusAndShortcuts(_model);
            ShowChosenView();
        }
    }

    /// <summary>
    /// The menu bar and the window's shortcuts, from the controller's commands: a command added
    /// to a controller shows up in its menu with its shortcut without a change here.
    /// </summary>
    private void BuildMenusAndShortcuts(MainViewModel model)
    {
        _adapters.Clear();
        KeyBindings.Clear();
        List<MenuItem> menus = [];
        Dictionary<string, MenuItem> modeMenus = [];

        MenuItem MenuOf(string name)
        {
            string top = MenuBar.Contains(name) ? name : "Modes";
            MenuItem menu = menus.FirstOrDefault(m => (string?)m.Tag == top) ?? Add(top);
            if (top != "Modes")
            {
                return menu;
            }
            if (!modeMenus.TryGetValue(name, out MenuItem? sub))
            {
                sub = new MenuItem { Header = name };
                menu.Items.Add(sub);
                modeMenus[name] = sub;
            }
            return sub;
        }

        MenuItem Add(string name)
        {
            MenuItem menu = new MenuItem { Header = "_" + name, Tag = name };
            menus.Add(menu);
            return menu;
        }

        foreach (string name in MenuBar)
        {
            Add(name);
        }
        foreach (EditorCommand command in model.Commands.All.Where(c => c.Menu.Length > 0))
        {
            MenuItem menu = MenuOf(command.Menu);
            if (command.SeparatorBefore && menu.Items.Count > 0)
            {
                menu.Items.Add(new Separator());
            }
            CommandAdapter adapter = new CommandAdapter(model, command);
            _adapters.Add(adapter);
            MenuItem item = new MenuItem { Header = command.Title, Command = adapter };
            if (command.Incomplete is IncompleteAttribute incomplete)
            {
                item.Classes.Add("planned");
                ToolTip.SetTip(item, $"Not built yet: user story {incomplete.Story}. {incomplete.Missing}");
            }
            if (command.Gesture is string gesture)
            {
                KeyGesture keys = KeyGesture.Parse(gesture);
                item.InputGesture = keys;
                KeyBindings.Add(new KeyBinding { Gesture = keys, Command = adapter });
            }
            menu.Items.Add(item);
        }
        KeyBindings.Add(new KeyBinding { Gesture = KeyGesture.Parse("Ctrl+Shift+Z"), Command = new CommandAdapter(model, model.Commands.Find("edit.redo")!) });
        MainMenu.ItemsSource = menus.Where(menu => menu.Items.Count > 0).ToList();
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
    /// A command of the controller as a menu item's or shortcut's command: it runs through the
    /// controller, so a command not built yet reports what it is to do.
    /// </summary>
    private sealed class CommandAdapter(MainViewModel model, EditorCommand command) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => command.IsIncomplete || command.CanRun();

        public void Execute(object? parameter) => _ = model.Commands.RunAsync(command.Id);

        public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
