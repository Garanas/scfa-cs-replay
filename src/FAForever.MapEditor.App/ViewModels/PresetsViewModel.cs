using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Workspaces;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// What the editor can save as a preset of a kind and apply again: a feature registers one per
/// kind (<see cref="PresetsViewModel.Register"/>).
/// </summary>
/// <param name="Kind">E.g. <c>brush</c>; the folder in the workspace's presets.</param>
/// <param name="Capture">The settings as they are now, and a name to suggest for them.</param>
/// <param name="Apply">Makes the settings of a preset the editor's.</param>
public sealed record PresetKind(string Kind, string Title, Func<(string Name, JsonObject Settings)> Capture, Action<Preset> Apply);

/// <summary>
/// The presets of a mode's panel, of one kind (<see cref="PresetsViewModel.Kind"/>).
/// </summary>
public sealed record PresetSection(string Kind, string Title) : PanelSection(Title)
{
    /// <summary>
    /// What the panel says while the kind has no presets: how to make one.
    /// </summary>
    public string EmptyText { get; init; } = "None yet: save the settings to use them again.";

    /// <summary>
    /// The Save button's label for screen readers and agents: what it keeps.
    /// </summary>
    public string SaveLabel { get; init; } = "Save the settings as a preset";
}

/// <summary>
/// The workspace's presets (user story F-10): saved settings of a kind (a brush, later a lighting,
/// water, units, markers, a palette of props) that are applied again. The files are in the
/// workspace (<see cref="PresetLibrary"/>), read again when another tool changes them; the panel
/// of a mode with a <see cref="PresetSection"/> lists those of its kind.
/// </summary>
public sealed partial class PresetsViewModel : ObservableObject
{
    private readonly ILogger _log;
    private readonly SynchronizationContext? _ui;
    private readonly Func<string, JsonObject, Task> _run;
    private readonly Dictionary<string, PresetKind> _kinds = new Dictionary<string, PresetKind>(StringComparer.Ordinal);
    private ProjectWatcher? _watcher;

    /// <param name="run">Runs a command with arguments, as a button does.</param>
    public PresetsViewModel(ILogger log, SynchronizationContext? ui, Func<string, JsonObject, Task> run)
    {
        _log = log;
        _ui = ui;
        _run = run;
    }

    /// <summary>
    /// The workspace's presets; null until the workspace is ready.
    /// </summary>
    public PresetLibrary? Library { get; private set; }

    /// <summary>
    /// Whether presets can be saved: the workspace is ready.
    /// </summary>
    public bool HasLibrary => Library is not null;

    /// <summary>
    /// The kinds the editor can save and apply.
    /// </summary>
    public IReadOnlyCollection<PresetKind> Kinds => _kinds.Values;

    /// <summary>
    /// The kind the selected mode's panel lists, e.g. <c>brush</c>; null when it lists none.
    /// </summary>
    [ObservableProperty]
    public partial string? Kind { get; private set; }

    /// <summary>
    /// The presets of <see cref="Kind"/>, by name.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial IReadOnlyList<Preset> Shown { get; private set; } = [];

    /// <summary>
    /// Whether the panel's kind has no presets yet.
    /// </summary>
    public bool IsEmpty => Shown.Count == 0;

    /// <summary>
    /// The name the panel's Save button gives a new preset; empty for the one suggested.
    /// </summary>
    [ObservableProperty]
    public partial string NewName { get; set; } = "";

    /// <exception cref="ArgumentException">The kind is registered already, or is no kind.</exception>
    public void Register(PresetKind kind)
    {
        if (!PresetLibrary.IsKind(kind.Kind) || !_kinds.TryAdd(kind.Kind, kind))
        {
            throw new ArgumentException($"The kind of preset '{kind.Kind}' is registered already or is no kind", nameof(kind));
        }
    }

    /// <summary>
    /// Uses the presets of a library, without watching it (tests).
    /// </summary>
    public void Use(PresetLibrary library)
    {
        Library = library;
        OnPropertyChanged(nameof(Library));
        OnPropertyChanged(nameof(HasLibrary));
        Refresh();
    }

    /// <summary>
    /// Uses the workspace's presets, and reads them again when a file changes there. Opening writes
    /// nothing: a workspace without a presets folder is watched once the first preset makes it.
    /// </summary>
    public void Open(Workspace workspace)
    {
        _watcher?.Dispose();
        _watcher = null;
        MoveOldFolders(workspace);
        Use(workspace.Presets);
        Watch();
    }

    /// <summary>
    /// What the last workspace opened moved from its old folders into <c>presets/</c> (<see cref="PresetFolders"/>).
    /// </summary>
    public IReadOnlyList<PresetMove> Moved { get; private set; } = [];

    /// <summary>
    /// Brings the presets of the old kind names and the map templates of <c>templates/</c> into the
    /// plural folders of <c>presets/</c>, once: a workspace without them stays as it is.
    /// </summary>
    private void MoveOldFolders(Workspace workspace)
    {
        try
        {
            Moved = PresetFolders.Move(workspace.Folder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Moved = [];
            _log.LogError(exception, "Moving the presets of {Folder} into presets failed: {Reason}", workspace.Folder, exception.Message);
            return;
        }
        foreach (PresetMove move in Moved)
        {
            _log.LogInformation("Moved the preset {From} to {To}", move.From, move.To);
        }
        if (Moved.Count > 0)
        {
            _log.LogInformation("Moved {Count} presets and templates into {Folder}", Moved.Count, workspace.PresetsFolder);
        }
    }

    private void Watch()
    {
        if (_watcher is not null || Library is not PresetLibrary library || !Directory.Exists(library.Folder))
        {
            return;
        }
        try
        {
            _watcher = new ProjectWatcher(library.Folder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _log.LogWarning(exception, "Cannot watch the presets in {Folder}: {Reason}", library.Folder, exception.Message);
            return;
        }
        _watcher.Changed += files => UiThread.Run(_ui, () =>
        {
            _log.LogInformation("The presets changed on disk: {Files}", string.Join(", ", files));
            Refresh();
        });
    }

    /// <summary>
    /// Lists the presets of the kind of a mode's panel.
    /// </summary>
    public void FollowMode(ModeDefinition mode) => FollowSections(mode.Sections);

    /// <summary>
    /// Lists the presets of the first preset section among the parts a mode's panel shows (those for
    /// the active tool and the chosen entry, <c>MainViewModel.PanelSections</c>).
    /// </summary>
    public void FollowSections(IEnumerable<PanelSection> sections)
    {
        string? kind = sections.OfType<PresetSection>().FirstOrDefault()?.Kind;
        if (kind == Kind && Kind is not null)
        {
            return;
        }
        Kind = kind;
        Refresh();
    }

    /// <summary>
    /// Shows the folder of the panel's kind, or of another, made when it is missing.
    /// </summary>
    /// <returns>The folder.</returns>
    /// <exception cref="InvalidOperationException">No workspace, or no kind.</exception>
    public string FolderOf(string? kind)
    {
        PresetLibrary library = Library ?? throw new InvalidOperationException("Presets need a workspace");
        string folder = library.FolderOf(kind ?? Kind ?? throw new InvalidOperationException("Name the kind of preset: this panel lists none"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>
    /// The panel's Show the folder.
    /// </summary>
    [RelayCommand]
    private Task ShowFolder() => _run("presets.showFolder", new JsonObject { ["kind"] = Kind });

    /// <summary>
    /// Reads the presets of <see cref="Kind"/> again.
    /// </summary>
    public void Refresh()
    {
        if (Library is null || Kind is null)
        {
            Shown = [];
            return;
        }
        List<string> unreadable = [];
        try
        {
            Shown = Library.List(Kind, unreadable);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _log.LogWarning(exception, "Cannot read the presets in {Folder}: {Reason}", Library.Folder, exception.Message);
            Shown = [];
        }
        foreach (string file in unreadable)
        {
            _log.LogWarning("A preset cannot be read: {File}", file);
        }
    }

    /// <summary>
    /// Saves the settings of a kind as they are now.
    /// </summary>
    /// <param name="kind">Null for the kind the panel lists.</param>
    /// <param name="name">Null or empty for the name the kind suggests, made unique.</param>
    /// <returns>The preset.</returns>
    /// <exception cref="InvalidOperationException">No workspace, or no kind to save.</exception>
    /// <exception cref="ArgumentException">The editor cannot save presets of that kind.</exception>
    public Preset Save(string? kind, string? name)
    {
        PresetLibrary library = Library ?? throw new InvalidOperationException("Presets need a workspace");
        PresetKind handler = Handler(kind ?? Kind ?? throw new InvalidOperationException("Choose a tool with settings to save, such as the sculpt or strata brush"));
        (string suggested, JsonObject settings) = handler.Capture();
        Preset preset = new Preset(handler.Kind, string.IsNullOrWhiteSpace(name) ? library.FreeName(handler.Kind, suggested) : name.Trim(), settings);
        string file = library.Save(preset);
        _log.LogInformation("Saved the preset {Name} ({Kind}) in {File}", preset.Name, preset.Kind, file);
        Refresh();
        Watch();
        return preset;
    }

    /// <summary>
    /// Applies a preset of the workspace.
    /// </summary>
    /// <exception cref="ArgumentException">There is no such preset, or the editor cannot apply its kind.</exception>
    /// <exception cref="MapProjectException">Its file cannot be read.</exception>
    public Preset Apply(string kind, string name)
    {
        PresetLibrary library = Library ?? throw new InvalidOperationException("Presets need a workspace");
        PresetKind handler = Handler(kind);
        Preset preset = library.Find(kind, name) ?? throw new ArgumentException($"There is no preset '{name}' of kind {kind}");
        handler.Apply(preset);
        return preset;
    }

    /// <summary>
    /// Deletes a preset of the workspace.
    /// </summary>
    /// <exception cref="ArgumentException">There is no such preset.</exception>
    public void Delete(string kind, string name)
    {
        PresetLibrary library = Library ?? throw new InvalidOperationException("Presets need a workspace");
        if (!library.Delete(kind, name))
        {
            throw new ArgumentException($"There is no preset '{name}' of kind {kind}");
        }
        _log.LogInformation("Deleted the preset {Name} ({Kind})", name, kind);
        Refresh();
    }

    /// <summary>
    /// The panel's Save button: saves under <see cref="NewName"/> through the command.
    /// </summary>
    [RelayCommand]
    private async Task SaveNew()
    {
        await _run("edit.saveAsPreset", new JsonObject { ["kind"] = Kind, ["name"] = NewName.Trim().Length == 0 ? null : NewName.Trim() });
        NewName = "";
    }

    /// <summary>
    /// A preset's button in the panel: applies it through the command.
    /// </summary>
    [RelayCommand]
    private Task ApplyShown(Preset preset) => _run("presets.apply", new JsonObject { ["kind"] = preset.Kind, ["name"] = preset.Name });

    /// <summary>
    /// A preset's delete button in the panel.
    /// </summary>
    [RelayCommand]
    private Task DeleteShown(Preset preset) => _run("presets.delete", new JsonObject { ["kind"] = preset.Kind, ["name"] = preset.Name });

    private PresetKind Handler(string kind) => _kinds.GetValueOrDefault(PresetKinds.Of(kind))
        ?? throw new ArgumentException($"The editor cannot save or apply presets of kind '{kind}' yet: {string.Join(", ", _kinds.Keys)}");
}
