using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// What one export is doing: the folder and version it writes, and the version of the map it
/// writes (<see cref="Document"/>), which steps change on their way.
/// </summary>
public sealed class ExportRun(MainViewModel model, string outputFolder, int? version, MapDocument document)
{
    public MainViewModel Model => model;

    public string OutputFolder => outputFolder;

    /// <summary>
    /// The version to write; null for the map's own.
    /// </summary>
    public int? Version => version;

    /// <summary>
    /// The map as the export writes it. A step that generates a part of the map (the normal map, the
    /// preview) puts it in the map itself (a step of undo) and here; a step that makes what only the
    /// game's files hold (walkability marks as terrain types, wave 9's generated path nodes) changes
    /// it here only, so the project stays as the map maker left it.
    /// </summary>
    public MapDocument Document { get; set; } = document;

    /// <summary>
    /// What a step says it did, shown beside it (e.g. <c>out of date: heightmap</c>); set by the step.
    /// </summary>
    public string? Note { get; set; }
}

/// <summary>
/// A step of the export (the Procedural mode, "What export does"): generating what is out of date
/// and needed, rendering the preview, writing the files. A feature adds its own with
/// <see cref="ExportViewModel.Register"/> at an <see cref="Order"/> between the editor's.
/// </summary>
/// <param name="Id">E.g. <c>preview</c>.</param>
/// <param name="Title">What the list says, e.g. <c>Render the preview</c>.</param>
/// <param name="Order">Where it runs: <see cref="GenerateOrder"/>, <see cref="PreviewOrder"/>,
/// <see cref="DecalsOrder"/>, <see cref="PathNodesOrder"/>, <see cref="GameFilesOrder"/>; the files are written after them all.</param>
public sealed record ExportStep(string Id, string Title, int Order)
{
    /// <summary>Generating the parts of the map that are out of date: the normal map.</summary>
    public const int GenerateOrder = 100;

    /// <summary>Rendering the preview, always.</summary>
    public const int PreviewOrder = 200;

    /// <summary>Wave 9: the map-wide normal and shadow decals, unless a file is chosen.</summary>
    public const int DecalsOrder = 300;

    /// <summary>Wave 9: the path nodes generated from the navigation mesh, written as markers.</summary>
    public const int PathNodesOrder = 400;

    /// <summary>What only the game's files hold, made from the map: walkability marks as terrain types.</summary>
    public const int GameFilesOrder = 500;

    /// <summary>Wave 10: the map's own textures in other formats converted to DDS, after every step that adds one.</summary>
    public const int TexturesOrder = 600;

    /// <summary>
    /// Whether the step has anything to do for this export; a step that has not shows as skipped.
    /// Null: always.
    /// </summary>
    public Func<ExportRun, bool>? Applies { get; init; }

    /// <summary>
    /// Does the step, on the window's thread (awaiting work on the pool), and may change
    /// <see cref="ExportRun.Document"/>.
    /// </summary>
    public required Func<ExportRun, Task> Run { get; init; }
}

/// <summary>
/// How a step of the export stands.
/// </summary>
public enum ExportStepStatus
{
    Waiting,
    Running,
    Done,
    Skipped,
    Failed,
}

/// <summary>
/// A line of the export's list: a step and how it stands in the export running or the last one.
/// </summary>
public sealed record ExportStepState(string Id, string Title, ExportStepStatus Status, string? Note)
{
    public bool IsRunning => Status == ExportStepStatus.Running;

    public bool IsFailed => Status == ExportStepStatus.Failed;

    public string StatusText => Status switch
    {
        ExportStepStatus.Waiting => "Waiting",
        ExportStepStatus.Running => "Running...",
        ExportStepStatus.Done => Note is null ? "Done" : $"Done: {Note}",
        ExportStepStatus.Skipped => Note is null ? "Not needed" : $"Not needed: {Note}",
        _ => Note is null ? "Failed" : $"Failed: {Note}",
    };
}

/// <summary>
/// The export as a list of steps, shown while it runs (the Procedural mode's Export section, the
/// status line) and kept for the last one: the registered steps in their order, then writing the
/// map folder.
/// </summary>
public sealed partial class ExportViewModel : ObservableObject
{
    /// <summary>
    /// The id of the last step, writing the files; no feature registers it.
    /// </summary>
    public const string WriteId = "write";

    private readonly List<ExportStep> _steps = [];

    /// <summary>
    /// The registered steps in the order they run.
    /// </summary>
    public IReadOnlyList<ExportStep> Steps => [.. _steps.OrderBy(step => step.Order)];

    /// <summary>
    /// Adds a step.
    /// </summary>
    /// <exception cref="ArgumentException">A step with its id is registered already.</exception>
    public void Register(ExportStep step)
    {
        if (step.Id == WriteId || _steps.Any(other => other.Id == step.Id))
        {
            throw new ArgumentException($"An export step '{step.Id}' is registered already", nameof(step));
        }
        _steps.Add(step);
        if (!IsRunning)
        {
            Lines = Planned();
        }
    }

    /// <summary>
    /// The steps of the export running, else of the last one, else what an export will do.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<ExportStepState> Lines { get; private set; } = [];

    [ObservableProperty]
    public partial bool IsRunning { get; private set; }

    /// <summary>
    /// The folder the last export wrote, or null.
    /// </summary>
    [ObservableProperty]
    public partial string? LastFolder { get; private set; }

    /// <summary>
    /// Forgets the last export: another map is open.
    /// </summary>
    public void Reset()
    {
        if (!IsRunning)
        {
            Lines = Planned();
            LastFolder = null;
        }
    }

    /// <summary>
    /// The lines of an export that has not run.
    /// </summary>
    public IReadOnlyList<ExportStepState> Planned() =>
        [.. Steps.Select(step => new ExportStepState(step.Id, step.Title, ExportStepStatus.Waiting, null)),
         new ExportStepState(WriteId, "Write the map folder", ExportStepStatus.Waiting, null)];

    /// <summary>
    /// Runs the steps in order, then <paramref name="write"/>; <paramref name="progress"/> hears of
    /// each step as it starts. A step that throws stops the export (it is marked failed, and the
    /// exception goes on).
    /// </summary>
    /// <returns>The map folder written.</returns>
    public async Task<string> RunAsync(ExportRun run, Func<ExportRun, Task<string>> write, Action<int, int, string>? progress = null)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("An export is running already");
        }
        IReadOnlyList<ExportStep> steps = Steps;
        List<ExportStepState> lines = [.. Planned()];
        IsRunning = true;
        Lines = [.. lines];
        try
        {
            for (int i = 0; i < steps.Count; i++)
            {
                ExportStep step = steps[i];
                run.Note = null;
                if (step.Applies is { } applies && !applies(run))
                {
                    lines[i] = lines[i] with { Status = ExportStepStatus.Skipped, Note = run.Note };
                    Lines = [.. lines];
                    continue;
                }
                progress?.Invoke(i + 1, steps.Count + 1, step.Title);
                lines[i] = lines[i] with { Status = ExportStepStatus.Running };
                Lines = [.. lines];
                try
                {
                    await step.Run(run);
                }
                catch (Exception exception)
                {
                    lines[i] = lines[i] with { Status = ExportStepStatus.Failed, Note = exception.Message };
                    Lines = [.. lines];
                    throw;
                }
                lines[i] = lines[i] with { Status = ExportStepStatus.Done, Note = run.Note };
                Lines = [.. lines];
            }

            int last = lines.Count - 1;
            progress?.Invoke(last + 1, lines.Count, lines[last].Title);
            lines[last] = lines[last] with { Status = ExportStepStatus.Running };
            Lines = [.. lines];
            try
            {
                string folder = await write(run);
                lines[last] = lines[last] with { Status = ExportStepStatus.Done, Note = Path.GetFileName(folder) };
                LastFolder = folder;
                return folder;
            }
            catch (Exception exception)
            {
                lines[last] = lines[last] with { Status = ExportStepStatus.Failed, Note = exception.Message };
                throw;
            }
        }
        finally
        {
            Lines = [.. lines];
            IsRunning = false;
        }
    }
}
