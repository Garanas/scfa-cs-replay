using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.Project.Workspaces;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// A line of the Console: what was logged, newest last.
/// </summary>
public sealed record ConsoleEntry(DateTimeOffset Time, LogLevel Level, string Category, string Message)
{
    public string TimeText => Time.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// <c>warning</c> or <c>error</c>; empty for information.
    /// </summary>
    public string LevelText => Level switch
    {
        LogLevel.Warning => "warning",
        LogLevel.Error or LogLevel.Critical => "error",
        LogLevel.Debug or LogLevel.Trace => "debug",
        _ => "",
    };

    public bool IsProblem => Level >= LogLevel.Warning;

    public bool IsWarning => Level == LogLevel.Warning;

    public bool IsError => Level >= LogLevel.Error;

    public override string ToString() => $"{TimeText} {(LevelText.Length > 0 ? LevelText + " " : "")}{Message}";
}

/// <summary>
/// What goes well and wrong (user story F-17): the loggers of the editor, the Console that shows
/// what they log, and the user's choices about recording and sending it (<see cref="Diagnostics.Telemetry"/>).
/// </summary>
public sealed partial class MainViewModel
{
    private const int ConsoleLines = 500;

    /// <summary>
    /// The key in an exception's data that says it is logged already.
    /// </summary>
    private const string Logged = "FAForever.MapEditor.Logged";

    /// <summary>
    /// Where the logs and metrics of this session go.
    /// </summary>
    public Telemetry Telemetry { get; }

    /// <summary>
    /// What happened, newest last: commands run, operations on files, warnings and errors.
    /// </summary>
    public ObservableCollection<ConsoleEntry> Console { get; } = [];

    /// <summary>
    /// The warnings and errors of <see cref="Console"/>.
    /// </summary>
    public ObservableCollection<ConsoleEntry> ConsoleProblems { get; } = [];

    /// <summary>
    /// Whether the Console shows only warnings and errors.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShownConsole))]
    public partial bool ConsoleShowsProblemsOnly { get; set; }

    public ObservableCollection<ConsoleEntry> ShownConsole => ConsoleShowsProblemsOnly ? ConsoleProblems : Console;

    /// <summary>
    /// The Console's tab: with the number of warnings and errors, when there are.
    /// </summary>
    public string ConsoleHeader => ConsoleProblems.Count > 0 ? $"Console ({ConsoleProblems.Count})" : "Console";

    /// <summary>
    /// Raised when the window is to show a folder in the system's file manager.
    /// </summary>
    public event Action<string>? ShowFolderRequested;

    [ObservableProperty]
    public partial bool IsDiagnosticsOpen { get; set; }

    /// <summary>
    /// Whether the session's logs and metrics are written to disk.
    /// </summary>
    public bool RecordDiagnostics
    {
        get => Telemetry.Settings.Record;
        set => ApplyTelemetry(Telemetry.Settings with { Record = value });
    }

    /// <summary>
    /// Whether what is recorded is sent to <see cref="DiagnosticsEndpoint"/>.
    /// </summary>
    public bool SendDiagnostics
    {
        get => Telemetry.Settings.Send;
        set => ApplyTelemetry(Telemetry.Settings with { Send = value });
    }

    /// <summary>
    /// The OpenTelemetry collector to send to (OTLP over HTTP, e.g. <c>http://localhost:4318</c>).
    /// </summary>
    public string DiagnosticsEndpoint
    {
        get => Telemetry.Settings.Endpoint ?? "";
        set => ApplyTelemetry(Telemetry.Settings with { Endpoint = string.IsNullOrWhiteSpace(value) ? null : value.Trim() });
    }

    public string DiagnosticsFolder => Telemetry.Folder ?? "nowhere: this editor keeps nothing on disk";

    /// <summary>
    /// What this session logged, and how sending goes.
    /// </summary>
    public string DiagnosticsSummary =>
        $"This session: warnings {Telemetry.Warnings}, errors {Telemetry.Errors}. " +
        (!Telemetry.Settings.Send ? "Nothing is sent."
            : Telemetry.Settings.Endpoint is null ? "Sending waits for an endpoint."
            : Telemetry.LastUploadError is string error ? $"Sending failed: {error}"
            : $"Sent to {Telemetry.Settings.Endpoint} every half minute.");

    /// <summary>
    /// A logger of the editor, by what it is about: <c>Commands</c>, <c>Files</c>, <c>Workspace</c>,
    /// <c>Rendering</c>, <c>Performance</c>, <c>Automation</c>, <c>Editor</c>.
    /// </summary>
    public ILogger Logger(string category) => Telemetry.CreateLogger(category);

    private ILogger FilesLog => Logger("Files");

    private ILogger WorkspaceLog => Logger("Workspace");

    private void OnLogged(LogEntry entry)
    {
        if (_ui is null || SynchronizationContext.Current == _ui)
        {
            AddToConsole(entry);
        }
        else
        {
            _ui.Post(_ => AddToConsole(entry), null);
        }
    }

    private void AddToConsole(LogEntry logged)
    {
        ConsoleEntry entry = new ConsoleEntry(logged.Time, logged.Level, logged.Category, logged.Message);
        Console.Add(entry);
        while (Console.Count > ConsoleLines)
        {
            Console.RemoveAt(0);
        }
        if (entry.IsProblem)
        {
            ConsoleProblems.Add(entry);
            while (ConsoleProblems.Count > ConsoleLines)
            {
                ConsoleProblems.RemoveAt(0);
            }
            OnPropertyChanged(nameof(ConsoleHeader));
            OnPropertyChanged(nameof(DiagnosticsSummary));
        }
    }

    /// <summary>
    /// Takes and remembers the user's choices about diagnostics.
    /// </summary>
    public void ApplyTelemetry(TelemetrySettings settings)
    {
        Telemetry.Apply(settings);
        try
        {
            _settings?.SetTelemetry(settings);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Logger("Editor").LogWarning("The choices about diagnostics could not be remembered: {Reason}", exception.Message);
        }
        OnPropertyChanged(nameof(RecordDiagnostics));
        OnPropertyChanged(nameof(SendDiagnostics));
        OnPropertyChanged(nameof(DiagnosticsEndpoint));
        OnPropertyChanged(nameof(DiagnosticsSummary));
    }

    /// <summary>
    /// Shows the choices about diagnostics.
    /// </summary>
    public void OpenDiagnostics()
    {
        OnPropertyChanged(nameof(DiagnosticsSummary));
        IsDiagnosticsOpen = true;
    }

    /// <summary>
    /// Shows the folder with the sessions' logs and metrics.
    /// </summary>
    public void ShowDiagnosticsFolder()
    {
        if (Telemetry.Folder is string folder)
        {
            Telemetry.Flush();
            Directory.CreateDirectory(folder);
            ShowFolderRequested?.Invoke(folder);
        }
    }

    [RelayCommand]
    private void CloseDiagnostics() => IsDiagnosticsOpen = false;
}
