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
/// What goes well and wrong (user story F-17): the Console that shows what the editor logs, and
/// the Diagnostics page with the user's choices about recording and sending it
/// (<see cref="Diagnostics.Telemetry"/>).
/// </summary>
public sealed partial class DiagnosticsViewModel : ObservableObject
{
    private const int ConsoleLines = 500;

    private readonly SynchronizationContext? _ui;
    private readonly Action<string> _showFolder;

    /// <param name="ui">The window's thread, which the Console is changed on; null in tests.</param>
    /// <param name="showFolder">Shows a folder in the system's file manager.</param>
    public DiagnosticsViewModel(Telemetry telemetry, SynchronizationContext? ui, Action<string> showFolder)
    {
        Telemetry = telemetry;
        _ui = ui;
        _showFolder = showFolder;
        telemetry.Logged += entry => UiThread.Run(_ui, () => AddToConsole(entry));
    }

    /// <summary>
    /// Where the logs and metrics of this session go.
    /// </summary>
    public Telemetry Telemetry { get; }

    /// <summary>
    /// Where the choices are remembered; set when the editor starts, null before (and in tests).
    /// </summary>
    public UserSettings? Settings { get; set; }

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
    /// Whether the Diagnostics page shows.
    /// </summary>
    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    /// <summary>
    /// Whether the session's logs and metrics are written to disk.
    /// </summary>
    public bool Record
    {
        get => Telemetry.Settings.Record;
        set => Apply(Telemetry.Settings with { Record = value });
    }

    /// <summary>
    /// Whether what is recorded is sent to <see cref="Endpoint"/>.
    /// </summary>
    public bool Send
    {
        get => Telemetry.Settings.Send;
        set => Apply(Telemetry.Settings with { Send = value });
    }

    /// <summary>
    /// The OpenTelemetry collector to send to (OTLP over HTTP, e.g. <c>http://localhost:4318</c>).
    /// </summary>
    public string Endpoint
    {
        get => Telemetry.Settings.Endpoint ?? "";
        set => Apply(Telemetry.Settings with { Endpoint = string.IsNullOrWhiteSpace(value) ? null : value.Trim() });
    }

    public string Folder => Telemetry.Folder ?? "nowhere: this editor keeps nothing on disk";

    /// <summary>
    /// What this session logged, and how sending goes.
    /// </summary>
    public string Summary =>
        $"This session: warnings {Telemetry.Warnings}, errors {Telemetry.Errors}. " +
        (!Telemetry.Settings.Send ? "Nothing is sent."
            : Telemetry.Settings.Endpoint is null ? "Sending waits for an endpoint."
            : Telemetry.LastUploadError is string error ? $"Sending failed: {error}"
            : $"Sent to {Telemetry.Settings.Endpoint} every half minute.");

    /// <summary>
    /// Takes and remembers the user's choices about diagnostics.
    /// </summary>
    public void Apply(TelemetrySettings settings)
    {
        Telemetry.Apply(settings);
        try
        {
            Settings?.SetTelemetry(settings);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Telemetry.CreateLogger("Editor").LogWarning("The choices about diagnostics could not be remembered: {Reason}", exception.Message);
        }
        OnPropertyChanged(nameof(Record));
        OnPropertyChanged(nameof(Send));
        OnPropertyChanged(nameof(Endpoint));
        OnPropertyChanged(nameof(Summary));
    }

    /// <summary>
    /// Shows the Diagnostics page.
    /// </summary>
    public void Open()
    {
        OnPropertyChanged(nameof(Summary));
        IsOpen = true;
    }

    /// <summary>
    /// Shows the folder with the sessions' logs and metrics.
    /// </summary>
    public void ShowFolder()
    {
        if (Telemetry.Folder is string folder)
        {
            Telemetry.Flush();
            Directory.CreateDirectory(folder);
            _showFolder(folder);
        }
    }

    [RelayCommand]
    private void Close() => IsOpen = false;

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
            OnPropertyChanged(nameof(Summary));
        }
    }
}
