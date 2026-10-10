using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.App.Diagnostics;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// Reporting a problem (user story F-17): a page over the window where the user says what went
/// wrong and whether the open map's project goes along, then a folder to write the report into;
/// the report (<see cref="ProblemReport"/>) shows in the file manager, ready to attach to an
/// issue. Nothing is sent.
/// </summary>
public sealed partial class ReportProblemViewModel(MainViewModel editor, IEditorDialogs dialogs) : ObservableObject
{
    private ILogger Log => editor.Logger("Editor");

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    /// <summary>
    /// What went wrong, in the user's words.
    /// </summary>
    [ObservableProperty]
    public partial string Description { get; set; } = "";

    /// <summary>
    /// Whether the open map's project goes into the report; off unless the user turns it on.
    /// </summary>
    [ObservableProperty]
    public partial bool IncludeProject { get; set; }

    /// <summary>
    /// Whether there is a project to put in: the map is saved as one.
    /// </summary>
    public bool CanIncludeProject => editor.Map?.ProjectFolder is not null;

    public string ProjectNote => CanIncludeProject
        ? editor.Map?.Session.IsModified == true
            ? "The project as it was last saved: save first to include the latest changes."
            : "The project's files, without its git history and what other tools build."
        : "The map is no project yet: save it as one to include it.";

    /// <summary>
    /// What the report will hold, in a line.
    /// </summary>
    public string Contents
    {
        get
        {
            int files = ProblemReport.SessionFiles(editor.Telemetry.Folder, ProblemReport.DefaultSessions).Length;
            return $"The logs and measurements of the last {ProblemReport.DefaultSessions} sessions ({files} files), the editor's version {Project.Workspaces.AssetConverter.EditorVersion} and the operating system, into one zip. Nothing is sent: attach the zip to an issue yourself.";
        }
    }

    /// <summary>
    /// The last report written.
    /// </summary>
    [ObservableProperty]
    public partial string? LastReport { get; private set; }

    [ObservableProperty]
    public partial string? Error { get; private set; }

    public void Open()
    {
        Error = null;
        if (!CanIncludeProject)
        {
            IncludeProject = false;
        }
        OnPropertyChanged(nameof(CanIncludeProject));
        OnPropertyChanged(nameof(ProjectNote));
        OnPropertyChanged(nameof(Contents));
        IsOpen = true;
    }

    /// <summary>
    /// Writes the report into <paramref name="folder"/> and shows it in the file manager.
    /// </summary>
    /// <param name="includeProject">Whether the open map's project goes in; the page's choice when left out.</param>
    /// <returns>The zip's path.</returns>
    public async Task<string> WriteAsync(string folder, string? description = null, bool? includeProject = null, int sessions = ProblemReport.DefaultSessions)
    {
        editor.Telemetry.Flush();
        string? project = (includeProject ?? IncludeProject) ? editor.Map?.ProjectFolder : null;
        if ((includeProject ?? IncludeProject) && project is null)
        {
            throw new InvalidOperationException("The map is no project yet: save it as one to include it");
        }
        ProblemReportContent content = new ProblemReportContent(
            description ?? Description,
            sessions,
            project,
            editor.Map?.Document.Scenario.Name,
            editor.Map?.Session.IsModified == true);
        string path;
        try
        {
            path = await Task.Run(() => ProblemReport.Write(folder, editor.Telemetry.Folder, content, DateTimeOffset.Now));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.LogError(exception, "Could not write the problem report into {Folder}: {Reason}", folder, exception.Message);
            throw new IOException($"Could not write the report: {exception.Message}", exception);
        }
        LastReport = path;
        Log.LogInformation("Wrote a problem report with {Sessions} sessions, project {Project}", sessions, project is not null);
        editor.Status = $"Wrote the report {Path.GetFileName(path)}: attach it to an issue";
        editor.ShowFolder(folder);
        return path;
    }

    [RelayCommand]
    private Task Make() => editor.TryAsync(async () =>
    {
        Error = null;
        string? folder = await dialogs.PickFolderAsync("Where to write the report", Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
        if (folder is null)
        {
            return;
        }
        try
        {
            await WriteAsync(folder);
            IsOpen = false;
            Description = "";
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            Error = exception.Message;
            throw;
        }
    });

    [RelayCommand]
    private void Close() => IsOpen = false;
}
