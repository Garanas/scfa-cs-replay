using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Controllers;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// A mode on the rail.
/// </summary>
public sealed partial class ModeItem(ModeDefinition definition) : ObservableObject
{
    public ModeDefinition Definition => definition;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>
    /// The tool last used in this mode, chosen again when the mode is.
    /// </summary>
    public string? LastTool { get; set; }
}

/// <summary>
/// A tool in the mode panel.
/// </summary>
public sealed partial class ToolItem(ToolDefinition definition) : ObservableObject
{
    public ToolDefinition Definition => definition;

    [ObservableProperty]
    public partial bool IsActive { get; set; }
}

/// <summary>
/// Something the views can show over the terrain; a chip above the views and the View menu.
/// </summary>
public sealed partial class OverlayItem(string id, string title, string story, Func<bool> get, Action<bool>? set) : ObservableObject
{
    public string Id => id;

    public string Title => title;

    public string Story => story;

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

    public void Refresh() => OnPropertyChanged(nameof(IsOn));
}

/// <summary>
/// The window's frame around the map (concept A of the UI design): the controller with its
/// commands, the modes and their tools, the overlays, the dock, the command palette and the
/// console. Things that are not built yet carry their user story (<see cref="IncompleteAttribute"/>,
/// <see cref="ToolDefinition.Missing"/>, <see cref="PlannedSection"/>).
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// The controller: every command of the editor (user story F-1).
    /// </summary>
    public CommandRegistry Commands { get; } = new CommandRegistry();

    public IReadOnlyList<ModeItem> Modes { get; private set; } = [];

    [ObservableProperty]
    public partial ModeItem? SelectedMode { get; private set; }

    /// <summary>
    /// The tools of the selected mode.
    /// </summary>
    public IReadOnlyList<ToolItem> Tools { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPaint), nameof(IsSculpting), nameof(IsPaintingStrata), nameof(IsToolIncomplete))]
    public partial ToolItem? ActiveTool { get; private set; }

    /// <summary>
    /// Whether the left mouse paints in the top view: the sculpt or strata tool is active.
    /// </summary>
    public bool CanPaint => HasMap && (IsSculpting || IsPaintingStrata);

    public bool IsSculpting => ActiveTool?.Definition.Id == "terrain.sculpt";

    public bool IsPaintingStrata => ActiveTool?.Definition.Id == "paint.strata";

    public bool IsToolIncomplete => ActiveTool?.Definition.IsIncomplete == true;

    public IReadOnlyList<OverlayItem> Overlays { get; private set; } = [];

    /// <summary>
    /// Which view shows: 0 the top view, 1 the 3D view.
    /// </summary>
    [ObservableProperty]
    public partial int ViewIndex { get; set; }

    /// <summary>
    /// Which tab of the dock shows: 0 Problems, 1 History, 2 Assets, 3 Console.
    /// </summary>
    [ObservableProperty]
    public partial int DockIndex { get; set; } = 1;

    /// <summary>
    /// Raised when the top view is to show the whole map.
    /// </summary>
    public event EventHandler? FitRequested;

    /// <summary>
    /// What happened, newest last: the commands run, and what was asked of things not built yet.
    /// </summary>
    public ObservableCollection<string> Console { get; } = [];

    [ObservableProperty]
    public partial bool IsPaletteOpen { get; set; }

    [ObservableProperty]
    public partial string PaletteQuery { get; set; } = "";

    [ObservableProperty]
    public partial IReadOnlyList<EditorCommand> PaletteResults { get; private set; } = [];

    [ObservableProperty]
    public partial EditorCommand? PaletteSelection { get; set; }

    private void InitializeShell()
    {
        foreach (object controller in (object[])
        [
            new FileController(this), new EditController(this), new ViewController(this), new MapController(this),
            new TerrainController(this), new PaintController(this), new AtmosphereController(this), new MarkersController(this),
            new PropsController(this), new DecalsController(this), new UnitsController(this), new GenerateController(this),
            new AssetsController(this), new PluginsController(this),
        ])
        {
            Commands.Add(controller);
        }
        Commands.IncompleteRun += command =>
        {
            Status = $"Not built yet: {command.FullTitle.TrimEnd('.')}. {command.Incomplete!.Missing} (user story {command.Incomplete.Story})";
            Log($"{command.Id}: not built yet, user story {command.Incomplete.Story}: {command.Incomplete.Missing}");
        };
        Commands.Ran += command => Log(command.Id);

        Modes = [.. EditorModes.All.Select(mode => new ModeItem(mode))];
        foreach (ModeItem mode in Modes)
        {
            foreach (ToolDefinition tool in mode.Definition.Tools)
            {
                string id = tool.Id;
                Commands.Register(new EditorCommand($"tool.{id}", tool.Title, mode.Definition.Title, false, null, tool.Story, null,
                    () => true, () =>
                    {
                        SelectTool(id);
                        return Task.CompletedTask;
                    }));
            }
        }

        Overlays =
        [
            new OverlayItem("markers", "Markers", "R-6", () => ShowMarkers, value => ShowMarkers = value),
            new OverlayItem("pathNodes", "Path nodes", "R-6", () => ShowPathNodes, value => ShowPathNodes = value),
            new OverlayItem("camera", "3D camera", "V-1", () => ShowCamera, value => ShowCamera = value),
            new OverlayItem("walkability", "Walkability", "T-7", () => false, null),
            new OverlayItem("mask", "Mask", "K-1", () => false, null),
            new OverlayItem("symmetry", "Symmetry", "F-12", () => false, null),
            new OverlayItem("grid", "Grid", "V-3", () => false, null),
            new OverlayItem("areas", "Areas", "M-5", () => false, null),
            new OverlayItem("props", "Props", "P-1", () => false, null),
            new OverlayItem("units", "Units", "U-2", () => false, null),
        ];

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
        Tools = [.. mode.Definition.Tools.Select(tool => new ToolItem(tool))];
        OnPropertyChanged(nameof(Tools));
        SelectTool(mode.LastTool ?? mode.Definition.Tools[0].Id);
    }

    /// <summary>
    /// Chooses a tool, in its own mode.
    /// </summary>
    /// <exception cref="ArgumentException">There is no such tool.</exception>
    public void SelectTool(string id)
    {
        ModeItem mode = Modes.FirstOrDefault(m => m.Definition.Tools.Any(t => t.Id == id))
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

        // the brush follows the tool: sculpting paints the heights, the strata tool a layer's mask
        if (IsSculpting && PaintedLayer != 0)
        {
            SelectPaintTarget(0);
        }
        else if (IsPaintingStrata && PaintedLayer == 0 && PaintTargets.Count > 1)
        {
            SelectPaintTarget(PaintTargets[1].Layer);
        }
    }

    /// <summary>
    /// The tool follows the brush, when automation or a test chooses what to paint.
    /// </summary>
    private void FollowPaintTarget()
    {
        if (Modes.Count == 0)
        {
            return;
        }
        if (PaintedLayer == 0 && !IsSculpting)
        {
            SelectTool("terrain.sculpt");
        }
        else if (PaintedLayer > 0 && !IsPaintingStrata)
        {
            SelectTool("paint.strata");
        }
    }

    public void RequestFit() => FitRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// The terrain layers whose masks the strata tool paints: all but the heights.
    /// </summary>
    public IReadOnlyList<PaintTarget> StrataTargets => [.. PaintTargets.Where(target => target.Layer > 0)];

    private void RefreshOverlays()
    {
        foreach (OverlayItem overlay in Overlays)
        {
            overlay.Refresh();
        }
    }

    private void Log(string line)
    {
        Console.Add($"{DateTime.Now:HH:mm:ss} {line}");
        while (Console.Count > 500)
        {
            Console.RemoveAt(0);
        }
    }

    partial void OnPaletteQueryChanged(string value) => SearchPalette();

    private void SearchPalette()
    {
        PaletteResults = [.. Commands.Search(PaletteQuery).Take(60)];
        PaletteSelection = PaletteResults.FirstOrDefault();
    }

    /// <summary>
    /// Opens the command palette (user story F-2), empty.
    /// </summary>
    public void OpenPalette()
    {
        PaletteQuery = "";
        SearchPalette();
        IsPaletteOpen = true;
    }

    /// <summary>
    /// Runs the command chosen in the palette, and closes it.
    /// </summary>
    public async Task RunPaletteSelectionAsync()
    {
        EditorCommand? command = PaletteSelection;
        IsPaletteOpen = false;
        if (command is not null)
        {
            await Commands.RunAsync(command.Id);
        }
    }

    /// <summary>
    /// Runs a command by its id, as a button, menu item, shortcut or automation does.
    /// </summary>
    [RelayCommand]
    private Task Run(string id) => Commands.RunAsync(id);

    [RelayCommand]
    private void ChooseMode(string id) => SelectMode(id);

    [RelayCommand]
    private void ChooseTool(string id) => SelectTool(id);

    [RelayCommand]
    private Task ToggleOverlay(OverlayItem overlay)
    {
        if (overlay.IsIncomplete)
        {
            overlay.Refresh();
            return Commands.RunAsync($"view.overlay.{overlay.Id}");
        }
        overlay.IsOn = !overlay.IsOn;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void ClosePalette() => IsPaletteOpen = false;

    [RelayCommand]
    private Task RunPalette() => RunPaletteSelectionAsync();
}
