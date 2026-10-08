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
}

/// <summary>
/// The editor's window without the window: the map being edited, the brush, and every operation
/// of the menu. The operations that take a folder (<see cref="OpenProject"/>, ...) are what the menu
/// commands call after asking for one, and what automation and tests call directly.
/// </summary>
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

    public string Title => Workspace is null
        ? "FAF map editor"
        : $"{(Workspace.Session.IsModified ? "* " : "")}{Workspace.Document.Scenario.Name} - {Workspace.ProjectFolder ?? "not saved as a project"} - FAF map editor";

    public bool HasMap => Workspace is not null;

    public bool CanUndo => Workspace?.Session.CanUndo == true && _stroke is null;

    public bool CanRedo => Workspace?.Session.CanRedo == true && _stroke is null;

    public string UndoHeader => Workspace?.Session.UndoDescription is string description ? $"_Undo {description}" : "_Undo";

    public string RedoHeader => Workspace?.Session.RedoDescription is string description ? $"_Redo {description}" : "_Redo";

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
    public void Open(string folder)
    {
        if (File.Exists(Path.Combine(folder, MapProjectLayout.Map)))
        {
            OpenProject(folder);
        }
        else
        {
            ImportMap(folder);
        }
    }

    public void OpenProject(string folder) => Attach(MapWorkspace.OpenProject(folder), $"Opened the project {folder}");

    public void ImportMap(string folder) => Attach(MapWorkspace.ImportMap(folder), $"Imported {folder}; save it as a project to keep it");

    public void Save()
    {
        MapWorkspace workspace = RequireMap();
        workspace.Save();
        Refresh($"Saved {workspace.ProjectFolder}");
    }

    public void SaveAs(string folder)
    {
        MapWorkspace workspace = RequireMap();
        workspace.SaveAs(folder);
        Watch(workspace.ProjectFolder!);
        Refresh($"Saved as {workspace.ProjectFolder}");
    }

    /// <returns>The map folder that was written.</returns>
    public string Export(string outputFolder, int? version = null)
    {
        string folder = RequireMap().Export(outputFolder, version);
        Status = $"Exported {folder}";
        return folder;
    }

    public bool Undo() => Workspace?.Session.Undo() == true;

    public bool Redo() => Workspace?.Session.Redo() == true;

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

    [RelayCommand]
    private async Task OpenProjectAsync()
    {
        if (await _dialogs.PickFolderAsync("Open a project") is string folder)
        {
            Try(() => OpenProject(folder));
        }
    }

    [RelayCommand]
    private async Task ImportMapAsync()
    {
        if (await _dialogs.PickFolderAsync("Import a map folder of the game") is string folder)
        {
            Try(() => ImportMap(folder));
        }
    }

    [RelayCommand(CanExecute = nameof(HasMap))]
    private async Task SaveProjectAsync()
    {
        if (Workspace?.ProjectFolder is null)
        {
            await SaveProjectAsAsync();
            return;
        }
        Try(Save);
    }

    [RelayCommand(CanExecute = nameof(HasMap))]
    private async Task SaveProjectAsAsync()
    {
        if (await _dialogs.PickFolderAsync("Save as a project in an empty folder") is string folder)
        {
            Try(() => SaveAs(folder));
        }
    }

    [RelayCommand(CanExecute = nameof(HasMap))]
    private async Task ExportMapAsync()
    {
        if (await _dialogs.PickFolderAsync("Export into a folder, e.g. the game's maps folder") is string folder)
        {
            Try(() => Export(folder));
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
                if (_stroke is null)
                {
                    ReloadPending();
                }
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

    private void ReloadPending()
    {
        if (Workspace is null || _pendingReload.Count == 0)
        {
            return;
        }
        string[] files = [.. _pendingReload];
        _pendingReload.Clear();
        Try(() =>
        {
            if (Workspace.Reload(files))
            {
                Status = $"Read again what changed on disk: {string.Join(", ", files)}";
            }
        });
    }

    private void OnSessionChanged(object? sender, EventArgs e) => Refresh(null);

    private void Refresh(string? status)
    {
        if (status is not null)
        {
            Status = status;
        }
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(HasMap));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(UndoHeader));
        OnPropertyChanged(nameof(RedoHeader));
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
    /// Runs an operation of the menu, showing what went wrong in the status line.
    /// </summary>
    private void Try(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception) when (exception is MapProjectException or IOException or InvalidOperationException or ArgumentException)
        {
            Status = exception.Message;
        }
    }
}
