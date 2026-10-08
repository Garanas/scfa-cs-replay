using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

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
    Task<string?> PickFolderAsync(string title);

    /// <summary>
    /// What to do with the unsaved changes of a map before it is closed.
    /// </summary>
    Task<SaveChoice> AskToSaveChangesAsync(string mapName);
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
/// The editor's window without the window: the map being edited, the brush, and every operation
/// of the menu. The operations that take a folder (<see cref="LoadProjectAsync"/>, ...) are what the
/// menu commands call after asking for one, and what automation and tests call directly.
/// </summary>
/// <remarks>
/// Opening, importing, saving and exporting read and write on a thread of the pool while the
/// editor is busy (<see cref="IsBusy"/>); painting goes on during a save, which writes the version
/// of the map from when it started.
/// </remarks>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly IEditorDialogs _dialogs;
    private readonly SynchronizationContext? _ui;
    private ProjectWatcher? _watcher;
    private EditTransaction? _stroke;
    private (float X, float Z) _lastDab;
    private readonly HashSet<string> _pendingReload = new HashSet<string>(StringComparer.Ordinal);

    public MainViewModel(IEditorDialogs dialogs)
    {
        _dialogs = dialogs;
        _ui = SynchronizationContext.Current;
    }

    /// <summary>
    /// The map being edited, or null before one is opened.
    /// </summary>
    public MapWorkspace? Workspace { get; private set; }

    /// <summary>
    /// The document as it is shown: with the brush stroke in progress, if there is one.
    /// </summary>
    public MapDocument? ShownDocument => _stroke?.Document ?? Workspace?.Document;

    /// <summary>
    /// Raised when another map is opened.
    /// </summary>
    public event EventHandler? WorkspaceChanged;

    /// <summary>
    /// Raised when the shown document changes: an edit, an undo, a reload, or a dab of a stroke.
    /// </summary>
    public event EventHandler? DocumentChanged;

    [ObservableProperty]
    public partial string Status { get; set; } = "Open a project or import a map folder of the game.";

    /// <summary>
    /// Whether a map is being opened, imported, saved or exported.
    /// </summary>
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public string Title => Workspace is null
        ? "FAF map editor"
        : $"{(Workspace.Session.IsModified ? "* " : "")}{Workspace.Document.Scenario.Name} - {Workspace.ProjectFolder ?? "not saved as a project"} - FAF map editor";

    public bool HasMap => Workspace is not null;

    public bool CanStart => !IsBusy;

    public bool CanSave => HasMap && !IsBusy;

    public bool CanUndo => Workspace?.Session.CanUndo == true && _stroke is null;

    public bool CanRedo => Workspace?.Session.CanRedo == true && _stroke is null;

    public string UndoHeader => Workspace?.Session.UndoDescription is string description ? $"_Undo {description}" : "_Undo";

    public string RedoHeader => Workspace?.Session.RedoDescription is string description ? $"_Redo {description}" : "_Redo";

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

    #region Brush

    public IReadOnlyList<HeightBrushMode> BrushModes { get; } = Enum.GetValues<HeightBrushMode>();

    [ObservableProperty]
    public partial HeightBrushMode BrushMode { get; set; } = HeightBrushMode.Raise;

    /// <summary>
    /// The radius in ogrids.
    /// </summary>
    [ObservableProperty]
    public partial double BrushRadius { get; set; } = 12;

    /// <summary>
    /// Game units at the centre for raising and lowering, 0 to 1 for smoothing and flattening.
    /// </summary>
    [ObservableProperty]
    public partial double BrushStrength { get; set; } = 0.5;

    /// <summary>
    /// The height in game units that flattening moves towards.
    /// </summary>
    [ObservableProperty]
    public partial double BrushTarget { get; set; } = 20;

    public HeightBrush Brush => new HeightBrush
    {
        Mode = BrushMode,
        Radius = (float)BrushRadius,
        Strength = (float)BrushStrength,
        Target = (float)BrushTarget,
    };

    #endregion

    #region Operations

    /// <summary>
    /// Opens a project, or imports a map folder of the game when the folder holds no project.
    /// </summary>
    public Task OpenAsync(string folder) => File.Exists(Path.Combine(folder, MapProjectLayout.Map))
        ? LoadProjectAsync(folder)
        : ImportMapFolderAsync(folder);

    public Task LoadProjectAsync(string folder) => BusyAsync($"Opening {folder}...", async () =>
        Attach(await MapWorkspace.OpenProjectAsync(folder), $"Opened the project {folder}"));

    public Task ImportMapFolderAsync(string folder) => BusyAsync($"Importing {folder}...", async () =>
        Attach(await MapWorkspace.ImportMapAsync(folder), $"Imported {folder}; save it as a project to keep it"));

    public Task SaveAsync() => BusyAsync("Saving...", async () =>
    {
        MapWorkspace workspace = RequireMap();
        await workspace.SaveAsync();
        Refresh($"Saved {workspace.ProjectFolder}");
    });

    public Task SaveAsAsync(string folder) => BusyAsync("Saving...", async () =>
    {
        MapWorkspace workspace = RequireMap();
        await workspace.SaveAsAsync(folder);
        Watch(workspace.ProjectFolder!);
        Refresh($"Saved as {workspace.ProjectFolder}");
    });

    /// <returns>The map folder that was written.</returns>
    public async Task<string> ExportAsync(string outputFolder, int? version = null)
    {
        string folder = "";
        await BusyAsync("Exporting...", async () =>
        {
            folder = await RequireMap().ExportAsync(outputFolder, version);
            Status = $"Exported {folder}";
        });
        return folder;
    }

    public bool Undo() => _stroke is null && Workspace?.Session.Undo() == true;

    public bool Redo() => _stroke is null && Workspace?.Session.Redo() == true;

    /// <summary>
    /// Undoes or redoes up to a step of the history (-1: the map as it was opened).
    /// </summary>
    public bool GoTo(int step) => _stroke is null && Workspace?.Session.GoTo(step) == true;

    /// <summary>
    /// Starts a brush stroke at a place of the map, in ogrids.
    /// </summary>
    public void BeginStroke(float x, float z)
    {
        MapWorkspace workspace = RequireMap();
        CancelStroke();
        _stroke = workspace.Session.Begin(BrushMode switch
        {
            HeightBrushMode.Raise => "Raise terrain",
            HeightBrushMode.Lower => "Lower terrain",
            HeightBrushMode.Smooth => "Smooth terrain",
            _ => "Flatten terrain",
        });
        Dab(x, z);
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Moves the stroke on, with dabs a quarter of the radius apart along the way.
    /// </summary>
    public void ContinueStroke(float x, float z)
    {
        if (_stroke is null)
        {
            return;
        }
        float spacing = Math.Max(0.5f, (float)BrushRadius / 4);
        float dx = x - _lastDab.X;
        float dz = z - _lastDab.Z;
        int steps = (int)(MathF.Sqrt(dx * dx + dz * dz) / spacing);
        if (steps == 0)
        {
            return;
        }
        (float x0, float z0) = _lastDab;
        for (int i = 1; i <= steps; i++)
        {
            Dab(x0 + dx * i / steps, z0 + dz * i / steps);
        }
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Ends the stroke: it becomes one step of the history.
    /// </summary>
    public void EndStroke()
    {
        if (_stroke is null)
        {
            return;
        }
        EditTransaction stroke = _stroke;
        _stroke = null;
        stroke.Commit();
        ReloadPending();
    }

    public void CancelStroke()
    {
        if (_stroke is null)
        {
            return;
        }
        EditTransaction stroke = _stroke;
        _stroke = null;
        stroke.Dispose();
        DocumentChanged?.Invoke(this, EventArgs.Empty);
        ReloadPending();
    }

    /// <summary>
    /// A whole stroke through places of the map, in ogrids.
    /// </summary>
    public void Stroke(IReadOnlyList<(float X, float Z)> points)
    {
        if (points.Count == 0)
        {
            return;
        }
        BeginStroke(points[0].X, points[0].Z);
        foreach ((float x, float z) in points.Skip(1))
        {
            ContinueStroke(x, z);
        }
        EndStroke();
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
        if (Workspace?.Session.IsModified != true)
        {
            return true;
        }
        switch (await _dialogs.AskToSaveChangesAsync(Workspace.Document.Scenario.Name))
        {
            case SaveChoice.Discard:
                return true;
            case SaveChoice.Save:
                await SaveProjectAsync();
                return !Workspace.Session.IsModified;
            default:
                return false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task OpenProjectAsync()
    {
        if (await ConfirmCloseAsync() && await _dialogs.PickFolderAsync("Open a project") is string folder)
        {
            await TryAsync(() => LoadProjectAsync(folder));
        }
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task ImportMapAsync()
    {
        if (await ConfirmCloseAsync() && await _dialogs.PickFolderAsync("Import a map folder of the game") is string folder)
        {
            await TryAsync(() => ImportMapFolderAsync(folder));
        }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveProjectAsync()
    {
        if (Workspace?.ProjectFolder is null)
        {
            await SaveProjectAsAsync();
            return;
        }
        await TryAsync(SaveAsync);
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveProjectAsAsync()
    {
        if (await _dialogs.PickFolderAsync("Save as a project in an empty folder") is string folder)
        {
            await TryAsync(() => SaveAsAsync(folder));
        }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task ExportMapAsync()
    {
        if (await _dialogs.PickFolderAsync("Export into a folder, e.g. the game's maps folder") is string folder)
        {
            await TryAsync(() => ExportAsync(folder));
        }
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void UndoEdit() => Undo();

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void RedoEdit() => Redo();

    #endregion

    private void Attach(MapWorkspace workspace, string status)
    {
        CancelStroke();
        if (Workspace is not null)
        {
            Workspace.Session.StateChanged -= OnSessionChanged;
        }
        Workspace = workspace;
        workspace.Session.StateChanged += OnSessionChanged;
        _pendingReload.Clear();
        if (workspace.ProjectFolder is string folder)
        {
            Watch(folder);
        }
        else
        {
            _watcher?.Dispose();
            _watcher = null;
        }
        WorkspaceChanged?.Invoke(this, EventArgs.Empty);
        Refresh(status);
    }

    /// <summary>
    /// Reads back what other tools change in the project folder, on the user interface's thread.
    /// </summary>
    private void Watch(string folder)
    {
        _watcher?.Dispose();
        _watcher = new ProjectWatcher(folder);
        _watcher.Changed += files =>
        {
            void Reload()
            {
                foreach (string file in files)
                {
                    _pendingReload.Add(file);
                }
                ReloadPending();
            }
            if (_ui is null)
            {
                Reload();
            }
            else
            {
                _ui.Post(_ => Reload(), null);
            }
        };
    }

    /// <summary>
    /// Reads the files that changed, unless a stroke or a save is going on: then after it.
    /// </summary>
    private void ReloadPending()
    {
        if (Workspace is null || _pendingReload.Count == 0 || _stroke is not null || IsBusy)
        {
            return;
        }
        string[] files = [.. _pendingReload];
        _pendingReload.Clear();
        try
        {
            if (Workspace.Reload(files))
            {
                Status = $"Read again what changed on disk: {string.Join(", ", files)}";
            }
        }
        catch (Exception exception) when (exception is MapProjectException or IOException or InvalidOperationException or ArgumentException)
        {
            Status = exception.Message;
        }
    }

    private void OnSessionChanged(object? sender, EventArgs e) => Refresh(null);

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
        IReadOnlyList<HistoryStep> steps = Workspace?.Session.History ?? [];
        int current = -1;
        for (int i = 0; i < steps.Count; i++)
        {
            if (steps[i].IsCurrent)
            {
                current = i;
            }
        }
        History = Workspace is null
            ? []
            : [new HistoryEntry(-1, "Opened", false), .. steps.Select((step, index) => new HistoryEntry(index, step.Description, index > current))];
        _showingHistory = true;
        OnPropertyChanged(nameof(History));
        HistoryIndex = Workspace is null ? -1 : current + 1;
        _showingHistory = false;
    }

    private void Refresh(string? status)
    {
        if (status is not null)
        {
            Status = status;
        }
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(HasMap));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(UndoHeader));
        OnPropertyChanged(nameof(RedoHeader));
        ShowHistory();
        OpenProjectCommand.NotifyCanExecuteChanged();
        ImportMapCommand.NotifyCanExecuteChanged();
        SaveProjectCommand.NotifyCanExecuteChanged();
        SaveProjectAsCommand.NotifyCanExecuteChanged();
        ExportMapCommand.NotifyCanExecuteChanged();
        UndoEditCommand.NotifyCanExecuteChanged();
        RedoEditCommand.NotifyCanExecuteChanged();
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Dab(float x, float z)
    {
        Brush.Apply(_stroke!.Heightmap, Workspace!.Document.Terrain.Heightmap.Scale, x, z);
        _lastDab = (x, z);
    }

    private MapWorkspace RequireMap() => Workspace ?? throw new InvalidOperationException("No map is open");

    /// <summary>
    /// Runs an operation that reads or writes in the background, with the editor busy meanwhile.
    /// </summary>
    /// <exception cref="InvalidOperationException">Another such operation is still running.</exception>
    private async Task BusyAsync(string status, Func<Task> operation)
    {
        if (IsBusy)
        {
            throw new InvalidOperationException("The editor is still busy with the previous operation");
        }
        IsBusy = true;
        Status = status;
        try
        {
            await operation();
        }
        finally
        {
            IsBusy = false;
            ReloadPending();
        }
    }

    /// <summary>
    /// Runs an operation of the menu, showing what went wrong in the status line.
    /// </summary>
    private async Task TryAsync(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (Exception exception) when (exception is MapProjectException or IOException or InvalidOperationException or ArgumentException)
        {
            Status = exception.Message;
        }
    }
}
