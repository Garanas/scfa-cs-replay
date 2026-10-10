using System.Diagnostics;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.Tools;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The questions the editor asks the person using it, which the window answers with its dialogs
/// and automation without them.
/// </summary>
public interface IEditorDialogs
{
    /// <summary>
    /// A folder the person picks, or null when they cancel.
    /// </summary>
    /// <param name="startIn">The folder the picker starts in, when it exists.</param>
    Task<string?> PickFolderAsync(string title, string? startIn = null);

    /// <summary>
    /// What to do with the unsaved changes of a map before it is closed.
    /// </summary>
    Task<SaveChoice> AskToSaveChangesAsync(string mapName);

    /// <summary>
    /// An asset of <paramref name="kind"/> the person chooses in the picker (user stories F-9,
    /// U-1), or null when they cancel. Without a picker of its own (a test's answers), it cancels.
    /// </summary>
    /// <param name="title">What the picker asks; <c>Choose a unit</c> and the like when null.</param>
    /// <param name="current">The key of the asset the picker starts on, such as the one a slot holds.</param>
    /// <returns>The chosen asset's key (<see cref="FAForever.MapEditor.Project.Workspaces.AssetEntry.Key"/>).</returns>
    Task<string?> PickAssetAsync(AssetKind kind, string? title = null, string? current = null) => Task.FromResult<string?>(null);

    /// <summary>
    /// A file to open that the person picks, or null when they cancel (user story T-3).
    /// </summary>
    /// <param name="extensions">The extensions it may have, e.g. <c>.png</c>, <c>.r16</c>.</param>
    Task<string?> PickFileAsync(string title, IReadOnlyList<string> extensions, string? startIn = null) => Task.FromResult<string?>(null);

    /// <summary>
    /// A file to write that the person picks, or null when they cancel (user story T-3).
    /// </summary>
    /// <param name="suggestedName">The name the dialog starts with, e.g. <c>heightmap.png</c>.</param>
    Task<string?> PickSaveFileAsync(string title, string suggestedName, IReadOnlyList<string> extensions, string? startIn = null) => Task.FromResult<string?>(null);
}

/// <summary>
/// The answers to <see cref="IEditorDialogs.AskToSaveChangesAsync"/>.
/// </summary>
public enum SaveChoice
{
    Save,
    Discard,
    Cancel,
}

/// <summary>
/// A line of the history panel.
/// </summary>
/// <param name="Step">The step of <see cref="EditSession.History"/>, -1 for the map as it was opened.</param>
/// <param name="IsUndone">Whether the step is undone: it can be redone.</param>
public sealed record HistoryEntry(int Step, string Description, bool IsUndone);

/// <summary>
/// The editor's window without the window: the map being edited, the tools, and every operation
/// of the menu. The operations that take a folder (<see cref="LoadProjectAsync"/>, ...) are what the
/// menu commands call after asking for one, and what automation and tests call directly.
/// </summary>
/// <remarks>
/// <para>
/// The parts with state of their own are classes it makes and holds: <see cref="Diagnostics"/>,
/// <see cref="Setup"/>, <see cref="VersionControl"/> and <see cref="Palette"/>. They write the
/// status line through it; the views bind through them (<c>{Binding Setup.Folder}</c>).
/// </para>
/// <para>
/// Opening, importing, saving and exporting read and write on a thread of the pool while the
/// editor is busy (<see cref="IsBusy"/>); painting goes on during a save, which writes the version
/// of the map from when it started.
/// </para>
/// </remarks>
public sealed partial class MainViewModel : ObservableObject
{
    /// <summary>
    /// The key in an exception's data that says it is logged already.
    /// </summary>
    private const string Logged = "FAForever.MapEditor.Logged";

    private readonly DialogGuard _dialogs;
    private readonly SynchronizationContext? _ui;
    private string? _openWhenReady;
    private ProjectWatcher? _watcher;
    private ProjectWatcher? _brushWatcher;
    private EditTransaction? _stroke;
    private readonly HashSet<string> _pendingReload = new HashSet<string>(StringComparer.Ordinal);

    /// <param name="telemetry">Where the logs and metrics go; without it the Console alone shows them.</param>
    public MainViewModel(IEditorDialogs dialogs, Telemetry? telemetry = null)
    {
        _dialogs = new DialogGuard(dialogs);
        _ui = SynchronizationContext.Current;
        Telemetry = telemetry ?? new Telemetry();
        Diagnostics = new DiagnosticsViewModel(Telemetry, _ui, ShowFolder);
        Setup = new WorkspaceSetupViewModel(_dialogs, Logger("Workspace"), text => Status = text);
        Setup.WorkspaceReady += OnWorkspaceReady;
        VersionControl = new VersionControlViewModel(this);
        Report = new ReportProblemViewModel(this, _dialogs);
        Assets = new AssetsViewModel(Logger("Assets"));
        Picker = new AssetPickerViewModel(() => Assets.Catalog);
        Events = new EditorEvents(Logger("Editor"));
        Symmetry = new SymmetryViewModel();
        Generation = new GenerationViewModel();
        Selection = new SelectionViewModel(RunWithAsync);
        Decals = new DecalsViewModel(RunWithAsync);
        Armies = new ArmiesViewModel(RunWithAsync);
        Markers = new MarkersViewModel(RunWithAsync);
        Commands = CommandServices.Create(this, _dialogs, typeof(MainViewModel).Assembly);
        Palette = new PaletteViewModel(Commands, RunAsync);
        Presets = new PresetsViewModel(Logger("Assets"), _ui, (id, arguments) => TryAsync(() => Commands.RunAsync(id, arguments, interactive: true)));
        InitializeShell();
        InitializeSelection();
        InitializeGeneration();
        InitializeDecals();
        InitializeUnits();
        InitializeMarkers();
        Props = new PropsViewModel(this);
        InitializePainting();
        InitializeMapPanels();
        InitializeMapSize();
        InitializeSymmetry();
        InitializeRules();
        InitializeHandles();
        InitializeLayout();
        InitializeAssetDiscovery();
        InitializeHeightLayers();
    }

    /// <summary>
    /// Where the logs and metrics of this session go.
    /// </summary>
    public Telemetry Telemetry { get; }

    /// <summary>
    /// The Console and the Diagnostics page (user story F-17).
    /// </summary>
    public DiagnosticsViewModel Diagnostics { get; }

    /// <summary>
    /// The start: the workspace, its setup page and the conversion of the assets (user story F-5).
    /// </summary>
    public WorkspaceSetupViewModel Setup { get; }

    /// <summary>
    /// The version control page of the open project (user story F-18).
    /// </summary>
    public VersionControlViewModel VersionControl { get; }

    /// <summary>
    /// The page that bundles a problem report (user story F-17).
    /// </summary>
    public ReportProblemViewModel Report { get; }

    /// <summary>
    /// The command palette (user story F-2).
    /// </summary>
    public PaletteViewModel Palette { get; }

    /// <summary>
    /// The asset shelf in the dock, and the asset the inspector shows (user stories F-8, F-9).
    /// </summary>
    public AssetsViewModel Assets { get; }

    /// <summary>
    /// The picker of one asset that a slot, tool or command opens (user stories F-9, U-1); the
    /// window's dialogs show it (<see cref="IEditorDialogs.PickAssetAsync"/>).
    /// </summary>
    public AssetPickerViewModel Picker { get; }

    /// <summary>
    /// What happens in the editor, for C# and later Lua plugins to react to (user story F-13).
    /// </summary>
    public EditorEvents Events { get; }

    /// <summary>
    /// The workspace's presets, and those the mode's panel lists (user story F-10).
    /// </summary>
    public PresetsViewModel Presets { get; }

    /// <summary>
    /// The symmetry edits are mirrored by, and whose guides the top view draws (user story F-12).
    /// </summary>
    public SymmetryViewModel Symmetry { get; }

    /// <summary>
    /// How the handles of the selection work: shown, the pivot, the snap (user story F-11).
    /// </summary>
    public HandleSettings Handles { get; } = new HandleSettings();

    /// <summary>
    /// What is generated when asked, and which results are out of date (user story F-7).
    /// </summary>
    public GenerationViewModel Generation { get; }

    /// <summary>
    /// The Props mode: how props are placed, the palette, following the terrain, the reclaim
    /// (user stories P-1, P-2, P-3, P-5).
    /// </summary>
    public PropsViewModel Props { get; }

    /// <summary>
    /// A logger of the editor, by what it is about: <c>Commands</c>, <c>Files</c>, <c>Workspace</c>,
    /// <c>Rendering</c>, <c>Performance</c>, <c>Automation</c>, <c>VersionControl</c>, <c>Assets</c>, <c>Editor</c>.
    /// </summary>
    public ILogger Logger(string category) => Telemetry.CreateLogger(category);

    private ILogger FilesLog => Logger("Files");

    /// <summary>
    /// Raised when the window is to show a folder in the system's file manager.
    /// </summary>
    public event Action<string>? ShowFolderRequested;

    public void ShowFolder(string folder) => ShowFolderRequested?.Invoke(folder);

    /// <summary>
    /// Starts the editor with the workspace remembered in <paramref name="settings"/>, or its setup.
    /// </summary>
    /// <param name="openFolder">A project or map folder to open once the workspace is ready.</param>
    public Task StartAsync(UserSettings settings, string? openFolder = null)
    {
        Diagnostics.Settings = settings;
        LoadLayout(settings);
        _openWhenReady = openFolder;
        return Setup.StartAsync(settings);
    }

    private void OnWorkspaceReady(Workspace workspace)
    {
        WatchBrushes(workspace);
        Presets.Open(workspace);
        _ = LoadAssetsAsync(workspace);
        Refresh(null);
        Events.OnWorkspaceOpened(workspace);
        if (_openWhenReady is string folder)
        {
            _openWhenReady = null;
            _ = OpenOnStartAsync(folder);
        }
        else if (Map is null && OpenSandboxWhenReady)
        {
            _ = OpenSandboxAsync(workspace);
        }
    }

    /// <summary>
    /// Whether the editor opens the workspace's sandbox when it starts without a folder (the
    /// application does; tests of the start leave it off).
    /// </summary>
    public bool OpenSandboxWhenReady { get; set; }

    /// <summary>
    /// Opens the workspace's sandbox (<see cref="SandboxMap"/>), writing it first when it is missing:
    /// the map a new user or an agent has to work on.
    /// </summary>
    public async Task OpenSandboxAsync(Workspace workspace)
    {
        string folder;
        try
        {
            folder = await Task.Run(() => SandboxMap.Ensure(workspace));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or MapProjectException)
        {
            Logger("Workspace").LogWarning(exception, "Could not write the sandbox into {Folder}: {Reason}", workspace.ProjectsFolder, exception.Message);
            Status = $"Could not write the sandbox: {exception.Message}";
            return;
        }
        if (Map is null)
        {
            await OpenOnStartAsync(folder);
        }
    }

    /// <summary>
    /// Reads the brushes again when a file of the workspace's brushes folder changes: the map
    /// maker adds and changes them with other tools (user stories F-6, T-2).
    /// </summary>
    private void WatchBrushes(Workspace workspace)
    {
        _brushWatcher?.Dispose();
        _brushWatcher = null;
        try
        {
            Directory.CreateDirectory(workspace.BrushesFolder);
            _brushWatcher = new ProjectWatcher(workspace.BrushesFolder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Logger("Assets").LogWarning(exception, "Cannot watch the brushes in {Folder}: {Reason}", workspace.BrushesFolder, exception.Message);
            return;
        }
        _brushWatcher.Changed += files => UiThread.Run(_ui, () =>
        {
            Logger("Assets").LogInformation("The brushes changed on disk: {Files}", string.Join(", ", files));
            _ = LoadAssetsAsync(workspace);
        });
    }

    /// <summary>
    /// Reads the workspace's assets into the shelf; what fails shows in the status line.
    /// </summary>
    public async Task LoadAssetsAsync(Workspace workspace)
    {
        try
        {
            await Assets.LoadAsync(workspace);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Status = $"Could not read the workspace's assets: {exception.Message}";
            Logger("Assets").LogError(exception, "Reading the assets of {Folder} failed: {Reason}", workspace.AssetsFolder, exception.Message);
        }
    }

    /// <summary>
    /// The map being edited, or null before one is opened.
    /// </summary>
    public EditedMap? Map { get; private set; }

    /// <summary>
    /// The document as it is shown: with the brush stroke in progress, if there is one.
    /// </summary>
    public MapDocument? ShownDocument => _stroke?.Document ?? Map?.Document;

    /// <summary>
    /// Raised when another map is opened.
    /// </summary>
    public event EventHandler? MapChanged;

    /// <summary>
    /// Raised when the shown document changes: an edit, an undo, a reload, a dab of a stroke, a
    /// stroke thrown away. The views draw the terrain again where it changed.
    /// </summary>
    public event EventHandler? DocumentChanged;

    /// <summary>
    /// Raised when an overlay is turned on or off. The views draw what lies over the terrain again;
    /// the terrain stays as it is.
    /// </summary>
    public event EventHandler? OverlaysChanged;

    [ObservableProperty]
    public partial string Status { get; set; } = "Open a project or import a map folder of the game.";

    /// <summary>
    /// Whether a map is being opened, imported, saved or exported.
    /// </summary>
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public string Title => Map is null
        ? "FAF map editor"
        : $"{(Map.Session.IsModified ? "* " : "")}{Map.Document.Scenario.Name}{(Map.Document.IsPublishable ? "" : " (sandbox, not for the vault)")} - {Map.ProjectFolder ?? "not saved as a project"} - FAF map editor";

    /// <summary>
    /// The open map's name, for the menu bar; empty without a map.
    /// </summary>
    public string MapName => Map?.Document.Scenario.Name ?? "";

    public bool HasMap => Map is not null;

    public bool CanStart => !IsBusy;

    public bool CanSave => HasMap && !IsBusy;

    public bool CanUndo => Map?.Session.CanUndo == true && _stroke is null;

    public bool CanRedo => Map?.Session.CanRedo == true && _stroke is null;

    /// <summary>
    /// Whether the editor has what a command needs (<see cref="CommandRegistry.CanRun"/>).
    /// </summary>
    public bool Meets(Needs needs) =>
        (!needs.HasFlag(Needs.Map) || HasMap)
        && (!needs.HasFlag(Needs.Idle) || !IsBusy)
        && (!needs.HasFlag(Needs.Undo) || CanUndo)
        && (!needs.HasFlag(Needs.Redo) || CanRedo)
        && (!needs.HasFlag(Needs.Workspace) || Setup.Workspace is not null)
        && (!needs.HasFlag(Needs.Project) || Map?.ProjectFolder is not null)
        && (!needs.HasFlag(Needs.NoStroke) || _stroke is null)
        && (!needs.HasFlag(Needs.Selection) || Selection.HasAny);

    /// <summary>
    /// The history panel: the map as it was opened, then every step, the undone ones last.
    /// </summary>
    public IReadOnlyList<HistoryEntry> History { get; private set; } = [];

    /// <summary>
    /// The line of <see cref="History"/> the document is at; choosing another undoes or redoes up to it.
    /// </summary>
    [ObservableProperty]
    public partial int HistoryIndex { get; set; } = -1;

    private bool _showingHistory;

    #region Overlays

    /// <summary>
    /// Whether the view shows the spawns, deposits, AI markers and weather.
    /// </summary>
    [ObservableProperty]
    public partial bool ShowMarkers { get; set; } = true;

    /// <summary>
    /// Whether the view shows the path nodes and their links (thousands on a large map).
    /// </summary>
    [ObservableProperty]
    public partial bool ShowPathNodes { get; set; }

    partial void OnShowMarkersChanged(bool value) => RefreshOverlays();

    partial void OnShowPathNodesChanged(bool value) => RefreshOverlays();

    #endregion

    #region Operations

    /// <summary>
    /// Opens a project, or imports a map folder of the game when the folder holds no project.
    /// </summary>
    public Task OpenAsync(string folder) => File.Exists(Path.Combine(folder, MapProjectLayout.Map))
        ? LoadProjectAsync(folder)
        : ImportMapFolderAsync(folder);

    public Task LoadProjectAsync(string folder) => BusyAsync("open", $"Opening {folder}...", async () =>
        Attach(await EditedMap.OpenProjectAsync(folder), $"Opened the project {folder}"));

    public Task ImportMapFolderAsync(string folder) => BusyAsync("import", $"Importing {folder}...", async () =>
        Attach(await EditedMap.ImportMapAsync(folder), $"Imported {folder}; save it as a project to keep it"));

    /// <exception cref="OperationVetoedException">A handler of <see cref="EditorEvents.BeforeSave"/> stopped it.</exception>
    public async Task SaveAsync()
    {
        EditedMap map = RequireMap();
        Events.CheckSave(map, map.ProjectFolder);
        await BusyAsync("save", "Saving...", async () =>
        {
            await map.SaveAsync();
            Refresh($"Saved {map.ProjectFolder}");
        });
        Events.OnMapSaved(map);
    }

    /// <exception cref="OperationVetoedException">A handler of <see cref="EditorEvents.BeforeSave"/> stopped it.</exception>
    public async Task SaveAsAsync(string folder)
    {
        EditedMap map = RequireMap();
        Events.CheckSave(map, folder);
        await BusyAsync("save-as", "Saving...", async () =>
        {
            await map.SaveAsAsync(folder);
            Watch(map.ProjectFolder!);
            Refresh($"Saved as {map.ProjectFolder}");
        });
        Events.OnMapSaved(map);
    }

    /// <returns>The map folder that was written.</returns>
    /// <exception cref="OperationVetoedException">A handler of <see cref="EditorEvents.BeforeExport"/> stopped it.</exception>
    public async Task<string> ExportAsync(string outputFolder, int? version = null, bool raise = false)
    {
        EditedMap map = RequireMap();
        Events.CheckExport(map, outputFolder);
        if (raise)
        {
            // the map keeps its new version: the next export and the project go on from it (M-8)
            int next = map.Document.NextVersion;
            EditMap($"Raise the version to {next}", document => document with { Version = next });
        }
        await RenderPreviewWhenStaleAsync();
        string folder = "";
        int? exported = version ?? map.Document.Version;
        await BusyAsync("export", "Exporting...", async () =>
        {
            folder = await map.ExportAsync(outputFolder, version);
            Status = $"Exported {folder} (version {exported?.ToString() ?? "none"}); exporting with raise makes version {(exported ?? 0) + 1}";
        });
        if (!map.Document.IsPublishable)
        {
            // to play it, which is fine; it may never go to the vault
            Logger("Files").LogWarning("Exported {Map}, which is the sandbox: it is for trying the editor and is not to be uploaded", map.Document.Scenario.Name);
            Status = $"Exported {folder}. This is the sandbox: play it, but it cannot be uploaded to the vault";
        }
        Events.OnMapExported(map, folder);
        return folder;
    }

    public bool Undo() => _stroke is null && Map?.Session.Undo() == true;

    public bool Redo() => _stroke is null && Map?.Session.Redo() == true;

    /// <summary>
    /// Undoes or redoes up to a step of the history (-1: the map as it was opened).
    /// </summary>
    public bool GoTo(int step) => _stroke is null && Map?.Session.GoTo(step) == true;

    /// <summary>
    /// The edit a tool holds while the mouse is down (a brush stroke), or null. It shows in the
    /// views; undo, reloads and the commands that edit wait until it ends.
    /// </summary>
    public EditTransaction? StrokeInProgress => _stroke;

    /// <summary>
    /// Starts the edit of a stroke (<see cref="BrushStroke"/>), which becomes one step of the history.
    /// </summary>
    /// <exception cref="InvalidOperationException">No map is open.</exception>
    public EditTransaction StartStroke(string description)
    {
        EditedMap map = RequireMap();
        CancelStroke();
        _stroke = map.Session.Begin(description);
        Commands.NotifyCanRunChanged();
        return _stroke;
    }

    /// <summary>
    /// Ends a stroke: kept, it becomes one step of the history; else it is thrown away. A stroke
    /// the editor already ended (another map was opened) is left as it is.
    /// </summary>
    public void EndStroke(EditTransaction stroke, bool keep = true)
    {
        if (_stroke != stroke)
        {
            return;
        }
        _stroke = null;
        if (keep)
        {
            // the props on terrain a height brush moved go with it, in the same step (P-1)
            Props.FollowStroke(stroke, Map!.Session.Document);
            stroke.Commit();
        }
        else
        {
            stroke.Dispose();
            ShowStroke();
        }
        Commands.NotifyCanRunChanged();
        ReloadPending();
        RunAfterStroke();
    }

    /// <summary>
    /// Says that the stroke in progress painted (<see cref="DocumentChanged"/>): the views show it.
    /// </summary>
    public void ShowStroke() => DocumentChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Input to the active tool: the left mouse down at the first place, dragged through the others
    /// and up at the last, in ogrids.
    /// </summary>
    /// <exception cref="InvalidOperationException">The active tool is not built yet.</exception>
    public void Stroke(IReadOnlyList<(float X, float Z)> points)
    {
        if (points.Count == 0)
        {
            return;
        }
        IMapTool tool = ActiveTool?.Tool ?? throw new InvalidOperationException($"The tool {ActiveTool?.Definition.Title} is not built yet");
        tool.Press(points[0].X, points[0].Z);
        foreach ((float x, float z) in points.Skip(1))
        {
            tool.Drag(x, z);
        }
        tool.Release();
    }

    #endregion

    #region Commands of the menu

    /// <summary>
    /// Opens what the editor was started with, showing what went wrong in the status line.
    /// </summary>
    public Task OpenOnStartAsync(string folder) => TryAsync(() => OpenAsync(folder));

    /// <summary>
    /// Asks what to do with unsaved changes before the map is closed, and saves when asked to.
    /// </summary>
    /// <returns>Whether the map may be closed.</returns>
    public async Task<bool> ConfirmCloseAsync()
    {
        if (Map?.Session.IsModified != true)
        {
            return true;
        }
        switch (await _dialogs.AskToSaveChangesAsync(Map.Document.Scenario.Name))
        {
            case SaveChoice.Discard:
                return true;
            case SaveChoice.Save:
                await TryAsync(() => SaveProjectAsync());
                return !Map.Session.IsModified;
            default:
                return false;
        }
    }

    /// <summary>
    /// Saves the project; a map that is no project yet is saved as one in <paramref name="folder"/>,
    /// or in a folder the editor asks for.
    /// </summary>
    public async Task SaveProjectAsync(string? folder = null)
    {
        if (RequireMap().ProjectFolder is not null && folder is null)
        {
            await SaveAsync();
        }
        else if ((folder ?? await _dialogs.PickFolderAsync("Save as a project in an empty folder", Setup.Workspace?.ProjectsFolder)) is string chosen)
        {
            await SaveAsAsync(chosen);
        }
    }

    #endregion

    private void Attach(EditedMap map, string status)
    {
        CancelStroke();
        if (Map is not null)
        {
            Map.Session.StateChanged -= OnSessionChanged;
            Map.Session.Changed -= OnDocumentChange;
        }
        Map = map;
        map.Session.StateChanged += OnSessionChanged;
        map.Session.Changed += OnDocumentChange;
        Generation.Forget();
        _afterStroke.Clear();
        _pendingReload.Clear();
        if (map.ProjectFolder is string folder)
        {
            Watch(folder);
        }
        else
        {
            _watcher?.Dispose();
            _watcher = null;
        }
        ResetCamera();
        MapChanged?.Invoke(this, EventArgs.Empty);
        Refresh(map.Document.IsPublishable ? status : $"{status}. This is the sandbox: try anything here; it can be exported to play, not uploaded to the vault");
        Events.OnMapOpened(map);
    }

    /// <summary>
    /// Reads back what other tools change in the project folder, on the user interface's thread.
    /// </summary>
    private void Watch(string folder)
    {
        _watcher?.Dispose();
        _watcher = new ProjectWatcher(folder);
        _watcher.Changed += files => UiThread.Run(_ui, () =>
        {
            foreach (string file in files)
            {
                _pendingReload.Add(file);
            }
            ReloadPending();
        });
    }

    /// <summary>
    /// Reads the files that changed, unless a stroke or a save is going on: then after it.
    /// </summary>
    private void ReloadPending()
    {
        if (Map is null || _pendingReload.Count == 0 || _stroke is not null || IsBusy)
        {
            return;
        }
        string[] files = [.. _pendingReload];
        _pendingReload.Clear();
        try
        {
            if (Map.Reload(files))
            {
                Status = $"Read again what changed on disk: {string.Join(", ", files)}";
                FilesLog.LogInformation("Read again what changed on disk: {Files}", string.Join(", ", files));
            }
        }
        catch (Exception exception) when (exception is MapProjectException or IOException or InvalidOperationException or ArgumentException)
        {
            Status = exception.Message;
            FilesLog.LogWarning(exception, "Could not read again {Files}: {Reason}", string.Join(", ", files), exception.Message);
        }
    }

    private void OnSessionChanged(object? sender, EventArgs e) => Refresh(null);

    private void OnDocumentChange(object? sender, DocumentChange change) => Events.OnDocumentChanged(change);

    partial void OnIsBusyChanged(bool value) => Refresh(null);

    partial void OnHistoryIndexChanged(int value)
    {
        if (!_showingHistory && value >= 0 && value < History.Count)
        {
            GoTo(History[value].Step);
        }
    }

    /// <summary>
    /// Shows the history of the session, with the line it is at chosen.
    /// </summary>
    private void ShowHistory()
    {
        using Performance.Span measure = Performance.Measure("model.history");
        IReadOnlyList<HistoryStep> steps = Map?.Session.History ?? [];
        int current = -1;
        for (int i = 0; i < steps.Count; i++)
        {
            if (steps[i].IsCurrent)
            {
                current = i;
            }
        }
        History = Map is null
            ? []
            : [new HistoryEntry(-1, "Opened", false), .. steps.Select((step, index) => new HistoryEntry(index, step.Description, index > current))];
        _showingHistory = true;
        OnPropertyChanged(nameof(History));
        HistoryIndex = Map is null ? -1 : current + 1;
        _showingHistory = false;
    }

    private void Refresh(string? status)
    {
        using Performance.Span measure = Performance.Measure("model.refresh");
        if (status is not null)
        {
            Status = status;
        }
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(MapName));
        OnPropertyChanged(nameof(HasMap));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        RefreshOverlays();
        ShowHistory();
        Assets.UseMap(Map?.Document);
        Generation.Refresh(Map?.Document);
        Commands.NotifyCanRunChanged();
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CancelStroke()
    {
        if (_stroke is EditTransaction stroke)
        {
            EndStroke(stroke, keep: false);
        }
    }

    private EditedMap RequireMap() => Map ?? throw new InvalidOperationException("No map is open");

    /// <summary>
    /// Runs an operation that reads or writes in the background, with the editor busy meanwhile;
    /// logs how long it took, or why it failed, and measures it
    /// (<see cref="EditorMetrics.OperationDuration"/>). For the parts that work on the open map.
    /// </summary>
    /// <param name="name">What it is, for the metrics: <c>open</c>, <c>save</c>, ...</param>
    /// <exception cref="InvalidOperationException">Another such operation is still running.</exception>
    internal async Task BusyAsync(string name, string status, Func<Task> operation)
    {
        if (IsBusy)
        {
            throw new InvalidOperationException("The editor is still busy with the previous operation");
        }
        IsBusy = true;
        Status = status;
        long start = Stopwatch.GetTimestamp();
        try
        {
            await operation();
            double milliseconds = Math.Round(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            EditorMetrics.OperationDuration.Record(milliseconds, EditorMetrics.Tag("operation", name), EditorMetrics.Tag("result", "ok"));
            FilesLog.LogInformation("{Status} ({Operation}, {Milliseconds} ms)", Status, name, milliseconds);
        }
        catch (Exception exception)
        {
            double milliseconds = Math.Round(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            EditorMetrics.OperationDuration.Record(milliseconds, EditorMetrics.Tag("operation", name), EditorMetrics.Tag("result", "failed"));
            Status = exception.Message;
            FilesLog.LogError(exception, "{Operation} failed after {Milliseconds} ms: {Reason}", name, milliseconds, exception.Message);
            exception.Data[Logged] = true;
            throw;
        }
        finally
        {
            IsBusy = false;
            ReloadPending();
        }
    }

    /// <summary>
    /// Runs an operation of the menu or a page, showing what went wrong in the status line.
    /// </summary>
    internal async Task TryAsync(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (Exception exception) when (exception is MapProjectException or IOException or InvalidOperationException or ArgumentException)
        {
            Status = exception.Message;
            if (!exception.Data.Contains(Logged))
            {
                Logger("Commands").LogWarning(exception, "{Reason}", exception.Message);
            }
        }
    }
}
