using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Tools;
using System.ComponentModel;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Workspaces;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The views of the map, in the order of their tabs (<c>MainWindow.axaml</c>).
/// </summary>
public enum MapViewTab
{
    Top,
    ThreeD,

    /// <summary>
    /// The top and 3D views side by side, sharing the camera (user story V-4).
    /// </summary>
    SideBySide,
}

/// <summary>
/// The tabs of the dock, in their order (<c>Views/Dock.axaml</c>).
/// </summary>
public enum DockTab
{
    Problems,
    History,
    Assets,
    Console,
}

/// <summary>
/// A mode on the rail.
/// </summary>
public sealed partial class ModeItem(ModeDefinition definition) : ObservableObject
{
    public ModeDefinition Definition => definition;

    /// <summary>
    /// Its button's tool tip: its title with its shortcut, and what it holds.
    /// </summary>
    public string Tip => $"{definition.Title} ({definition.Gesture}): {definition.Help}";

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>
    /// The tool last used in this mode, chosen again when the mode is.
    /// </summary>
    public string? LastTool { get; set; }

    /// <summary>
    /// The tools plugins added to the mode (user story F-15), after its own.
    /// </summary>
    public List<ToolDefinition> PluginTools { get; } = [];

    /// <summary>
    /// Its own tools, then the plugins'.
    /// </summary>
    public IEnumerable<ToolDefinition> AllTools => definition.Tools.Concat(PluginTools);
}

/// <summary>
/// A tool in the mode panel.
/// </summary>
/// <param name="tool">What it does; null while it is not built.</param>
public sealed partial class ToolItem(ToolDefinition definition, IMapTool? tool) : ObservableObject
{
    public ToolDefinition Definition => definition;

    public IMapTool? Tool => tool;

    [ObservableProperty]
    public partial bool IsActive { get; set; }
}

/// <summary>
/// Something the views can show over the terrain; a chip above the views and the View menu.
/// </summary>
/// <param name="set">Null while the views cannot show it (<see cref="OverlayDefinition.Missing"/>).</param>
public sealed partial class OverlayItem(OverlayDefinition definition, Func<bool> get, Action<bool>? set, Func<string>? detail = null) : ObservableObject
{
    public OverlayDefinition Definition => definition;

    public string Id => definition.Id;

    public string Title => definition.Title;

    /// <summary>
    /// What its chip says: its title, and a setting of it such as the grid's spacing (<c>Grid 16</c>).
    /// </summary>
    public string Text => detail is null ? definition.Title : $"{definition.Title} {detail()}";

    public string Story => definition.Story;

    /// <summary>
    /// Whether the views cannot show it yet.
    /// </summary>
    public bool IsIncomplete => set is null;

    public bool IsOn
    {
        get => get();
        set
        {
            if (set is not null && value != get())
            {
                set(value);
                OnPropertyChanged();
            }
        }
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(IsOn));
        OnPropertyChanged(nameof(Text));
    }
}

/// <summary>
/// The window's frame around the map (concept A of the UI design): the controller with its
/// commands, the modes and their tools, the overlays and the tabs. Things that are not built yet
/// carry their user story (<see cref="IncompleteAttribute"/>, <see cref="ToolDefinition.Missing"/>,
/// <see cref="PlannedSection"/>).
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// The controller: every command of the editor (user story F-1).
    /// </summary>
    public CommandRegistry Commands { get; }

    public IReadOnlyList<ModeItem> Modes { get; private set; } = [];

    /// <summary>
    /// The modes at the rail's top: those of the map, its look and its things.
    /// </summary>
    public IReadOnlyList<ModeItem> RailModes => [.. Modes.Where(mode => mode.Definition.Group < EditorModes.BottomGroup)];

    /// <summary>
    /// The modes at the rail's bottom, below a divider: the Procedural and Plugins modes.
    /// </summary>
    public IReadOnlyList<ModeItem> BottomRailModes => [.. Modes.Where(mode => mode.Definition.Group >= EditorModes.BottomGroup)];

    [ObservableProperty]
    public partial ModeItem? SelectedMode { get; private set; }

    /// <summary>
    /// The tools of the selected mode.
    /// </summary>
    public IReadOnlyList<ToolItem> Tools { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsToolIncomplete), nameof(ShownMask))]
    public partial ToolItem? ActiveTool { get; private set; }

    public bool IsToolIncomplete => ActiveTool?.Definition.IsIncomplete == true;

    /// <summary>
    /// The tools that are built, made once (<see cref="ToolDefinition.Make"/>), by id.
    /// </summary>
    private readonly Dictionary<string, IMapTool> _tools = new Dictionary<string, IMapTool>(StringComparer.Ordinal);

    /// <summary>
    /// The tool of a class, whether it is active or not: for the commands that set it up, and tests.
    /// </summary>
    public T Tool<T>() where T : IMapTool => _tools.Values.OfType<T>().Single();

    /// <summary>
    /// The strata tool, whose layer the Paint mode's panel chooses.
    /// </summary>
    public StrataTool StrataTool => Tool<StrataTool>();

    /// <summary>
    /// The terrain layer whose mask the top view shows: the strata tool's while it is active, else 0.
    /// </summary>
    public int ShownMask => (ActiveTool?.Tool as StrataTool)?.Layer ?? 0;

    partial void OnActiveToolChanged(ToolItem? value) => CursorChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Raised when a tool's settings change what the views draw around the mouse: its radius, its
    /// brush's shape or rotation.
    /// </summary>
    public event EventHandler? CursorChanged;

    /// <summary>
    /// The catalogue the brush tools last read their shapes from.
    /// </summary>
    private AssetCatalog? _brushCatalog;

    /// <summary>
    /// The brush tools offer the workspace's brushes as shapes, and a brush chosen on the shelf
    /// becomes the active tool's shape (user stories T-1, T-2, S-1).
    /// </summary>
    private void OnAssetsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AssetsViewModel.Kinds) && Assets.Catalog != _brushCatalog)
        {
            _brushCatalog = Assets.Catalog;
            foreach (IShapedBrushTool tool in _tools.Values.OfType<IShapedBrushTool>())
            {
                tool.Shape.Refresh(Assets.Catalog);
            }
        }
        else if (e.PropertyName == nameof(AssetsViewModel.Shown)
            && Assets.Shown is { Kind: AssetKind.Brush } brush
            && ActiveTool?.Tool is IShapedBrushTool active)
        {
            active.Shape.Choose(brush.Key);
        }
    }

    public IReadOnlyList<OverlayItem> Overlays { get; private set; } = [];

    /// <summary>
    /// Which view of the map shows.
    /// </summary>
    [ObservableProperty]
    public partial MapViewTab View { get; set; }

    /// <summary>
    /// Which tab of the dock shows.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDockTall))]
    public partial DockTab Dock { get; set; } = DockTab.History;

    /// <summary>
    /// Whether the dock is higher: while it shows the asset shelf.
    /// </summary>
    public bool IsDockTall => Dock == DockTab.Assets;

    /// <summary>
    /// The overlays the selected mode's panel lists (<see cref="OverlayDefinition.Panel"/>).
    /// </summary>
    public IReadOnlyList<OverlayItem> PanelOverlays =>
        [.. Overlays.Where(overlay => overlay.Definition.Panel is string panel && panel == SelectedMode?.Definition.Id)];

    partial void OnSelectedModeChanged(ModeItem? value) => OnPropertyChanged(nameof(PanelOverlays));

    /// <summary>
    /// Raised when the top view is to show the whole map.
    /// </summary>
    public event EventHandler? FitRequested;

    private void InitializeShell()
    {
        ILogger commandsLog = Logger("Commands");
        Commands.IncompleteRun += command =>
        {
            Status = $"Not built yet: {command.FullTitle.TrimEnd('.')}. {command.Incomplete!.Missing} (user story {command.Incomplete.Story})";
            commandsLog.LogInformation("{Command}: not built yet, user story {Story}: {Missing}", command.Id, command.Incomplete.Story, command.Incomplete.Missing);
        };
        Commands.Ran += command => commandsLog.LogInformation("Ran {Command}", command.Id);

        foreach (ToolDefinition tool in EditorModes.All.SelectMany(mode => mode.Tools))
        {
            if (tool.Make is not null)
            {
                _tools[tool.Id] = tool.Make(this);
            }
        }

        Assets.PropertyChanged += OnAssetsChanged;
        Presets.Register(BrushPreset.Of(this));
        Presets.Register(BrushPreset.Stamps(this));
        Symmetry.Changed += (_, _) => RefreshOverlays();
        foreach (IMapTool tool in _tools.Values)
        {
            if (tool is INotifyPropertyChanged settings)
            {
                settings.PropertyChanged += (_, e) =>
                {
                    if (tool is StrataTool && e.PropertyName == nameof(StrataTool.Layer))
                    {
                        OnPropertyChanged(nameof(ShownMask));
                    }
                    CursorChanged?.Invoke(this, EventArgs.Empty);
                };
            }
            if (tool is IShapedBrushTool shaped)
            {
                shaped.Shape.PropertyChanged += (_, _) => CursorChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        // the commands made from data: a mode, a tool, an overlay each
        Modes = [.. EditorModes.All.Select(mode => new ModeItem(mode))];
        foreach (ModeDefinition mode in EditorModes.All)
        {
            Commands.Register(new EditorCommand
            {
                Id = mode.CommandId, Title = $"{mode.Title} mode", Gesture = mode.Gesture, Story = mode.Story,
                MakeRequest = _ => new ChooseMode(mode.Id),
            });
            foreach (ToolDefinition tool in mode.Tools)
            {
                Commands.Register(new EditorCommand
                {
                    Id = tool.CommandId, Title = tool.Title, Story = tool.Story,
                    MakeRequest = _ => new ChooseTool(tool.Id),
                });
            }
        }

        Overlays = [.. EditorOverlays.All.Select(overlay => overlay.Id switch
        {
            "markers" => new OverlayItem(overlay, () => ShowMarkers, value => ShowMarkers = value),
            "pathNodes" => new OverlayItem(overlay, () => ShowPathNodes, value => ShowPathNodes = value),
            "camera" => new OverlayItem(overlay, () => ShowCamera, value => ShowCamera = value),
            "walkability" => new OverlayItem(overlay, () => ShowWalkability, value => ShowWalkability = value),
            "symmetry" => new OverlayItem(overlay, () => ShowSymmetry, value => ShowSymmetry = value),
            "props" => new OverlayItem(overlay, () => ShowProps, value => ShowProps = value),
            "units" => new OverlayItem(overlay, () => ShowUnits, value => ShowUnits = value),
            "armyColours" => new OverlayItem(overlay, () => ShowArmyColours, value => ShowArmyColours = value),
            "areas" => new OverlayItem(overlay, () => ShowAreas, value => ShowAreas = value),
            "mask" => new OverlayItem(overlay, () => ShowMask, value => ShowMask = value),
            "grid" => new OverlayItem(overlay, () => ShowGrid, value => ShowGrid = value, () => EditorCulture.Format(GridSpacing)),
            "decals" => new OverlayItem(overlay, () => ShowDecals, value => ShowDecals = value),
            "navigation" => new OverlayItem(overlay, () => ShowNavigation, value => ShowNavigation = value),
            "generatedPathNodes" => new OverlayItem(overlay, () => ShowGeneratedPathNodes, value => ShowGeneratedPathNodes = value),
            _ => new OverlayItem(overlay, () => false, null),
        })];
        foreach (OverlayDefinition overlay in EditorOverlays.All)
        {
            Commands.Register(new EditorCommand
            {
                Id = overlay.CommandId, Title = overlay.CommandTitle, Story = overlay.Story,
                Incomplete = overlay.Missing is string missing ? new IncompleteAttribute(overlay.Story, missing) : null,
                Parameters = [new CommandParameter("on", typeof(bool?), true)],
                MakeRequest = overlay.IsIncomplete ? null : arguments => new ToggleOverlay(overlay.Id, arguments["on"]?.GetValue<bool>()),
            });
        }

        Commands.Arrange(Menus.Bar);
        SelectMode("terrain");
    }

    /// <summary>
    /// Chooses a mode on the rail, and the tool last used in it (else its first).
    /// </summary>
    /// <exception cref="ArgumentException">There is no such mode.</exception>
    public void SelectMode(string id)
    {
        ModeItem mode = Modes.FirstOrDefault(m => m.Definition.Id == id)
            ?? throw new ArgumentException($"There is no mode '{id}': {string.Join(", ", Modes.Select(m => m.Definition.Id))}");
        foreach (ModeItem m in Modes)
        {
            m.IsSelected = m == mode;
        }
        SelectedMode = mode;
        Assets.FollowMode(id);
        Presets.FollowMode(mode.Definition);
        Tools = [.. mode.AllTools.Select(tool => new ToolItem(tool, _tools.GetValueOrDefault(tool.Id)))];
        OnPropertyChanged(nameof(Tools));
        SelectTool(mode.LastTool is string last && mode.AllTools.Any(tool => tool.Id == last) ? last : mode.Definition.Tools[0].Id);
    }

    /// <summary>
    /// Chooses a tool, in its own mode.
    /// </summary>
    /// <exception cref="ArgumentException">There is no such tool.</exception>
    public void SelectTool(string id)
    {
        ModeItem mode = Modes.FirstOrDefault(m => m.AllTools.Any(t => t.Id == id))
            ?? throw new ArgumentException($"There is no tool '{id}'");
        if (SelectedMode != mode)
        {
            mode.LastTool = id;
            SelectMode(mode.Definition.Id);
            return;
        }
        mode.LastTool = id;
        foreach (ToolItem tool in Tools)
        {
            tool.IsActive = tool.Definition.Id == id;
        }
        ActiveTool = Tools.First(tool => tool.IsActive);
    }

    public void RequestFit() => FitRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Raised when the top view is to centre on a place of the map, in ogrids, at a zoom; what is
    /// null stays as it is (the place it centres on, the zoom).
    /// </summary>
    public event Action<double?, double?, double?>? TopViewRequested;

    public void RequestTopView(double? x, double? z, double? zoom) => TopViewRequested?.Invoke(x, z, zoom);

    /// <summary>
    /// What the 3D view's terrain shows: 0 the picture, 1 to 7 a part of it, to find out why it looks wrong.
    /// </summary>
    [ObservableProperty]
    public partial int GpuDebug { get; set; }

    /// <summary>
    /// An overlay was turned on or off: its chip and check box show it, the views draw it.
    /// </summary>
    private void RefreshOverlays()
    {
        foreach (OverlayItem overlay in Overlays)
        {
            overlay.Refresh();
        }
        OverlaysChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Runs a command by its id, as a button, menu item or shortcut does: what goes wrong shows in
    /// the status line (and the Console) rather than stopping the editor.
    /// </summary>
    [RelayCommand]
    public Task RunAsync(string id) => TryAsync(() => Commands.RunAsync(id));
}
