using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using FAForever.MapEditor.Project.Workspaces;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.Diagnostics;

/// <summary>
/// A line logged: what the Console shows and the logs on disk hold.
/// </summary>
/// <param name="Template">The message with its holes (<c>Opened {Folder}</c>), the same for every line of its kind.</param>
/// <param name="Attributes">The values in the holes, by name.</param>
public sealed record LogEntry(
    DateTimeOffset Time,
    LogLevel Level,
    string Category,
    string Message,
    string? Template,
    IReadOnlyList<KeyValuePair<string, object?>> Attributes,
    Exception? Exception);

/// <summary>
/// What goes well and wrong while the editor runs, kept as a service keeps it for Loki and
/// Prometheus: logs (every <see cref="ILogger"/> of the editor, which the Console shows too) and
/// metrics (<see cref="EditorMetrics"/>). Each session writes them to two files in
/// <see cref="Folder"/>, in OpenTelemetry's JSON (OTLP, one request a line):
/// <c>&lt;start&gt;-&lt;session&gt;.logs.jsonl</c> and <c>.metrics.jsonl</c>. A collector reads
/// those files as they are (its <c>otlpjsonfile</c> receiver), and with sending on they go line by
/// line to its OTLP endpoint.
/// </summary>
/// <remarks>
/// <para>
/// Recording is on unless the user turns it off; sending is off unless the user turns it on, and
/// then sends only what is recorded from that moment. The user's home folder is written as
/// <c>~</c>. The newest <see cref="KeptSessions"/> sessions are kept, each file up to
/// <see cref="MaxFileBytes"/>.
/// </para>
/// <para>
/// Logs are written every two seconds and at once for a crash; the metrics, cumulative since
/// the session started, every minute and when the editor closes.
/// </para>
/// </remarks>
public sealed class Telemetry : ILoggerProvider
{
    public const int KeptSessions = 30;

    public const long MaxFileBytes = 20L * 1024 * 1024;

    /// <summary>
    /// The service's name in Loki and Prometheus (<c>service_name</c>, <c>job</c>).
    /// </summary>
    public const string ServiceName = "faf-map-editor";

    private readonly ConcurrentDictionary<string, ILogger> _loggers = new ConcurrentDictionary<string, ILogger>(StringComparer.Ordinal);
    private readonly ConcurrentQueue<LogEntry> _pending = new();
    private readonly object _fileGate = new object();
    private readonly MetricCollector? _metrics;
    private readonly Timer? _timer;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _uploading = new SemaphoreSlim(1, 1);
    private readonly string _home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private long _ticks;
    private int _warnings;
    private int _errors;
    private bool _disposed;

    /// <param name="folder">Where the sessions are written; null keeps nothing on disk.</param>
    /// <param name="settings">The user's choices; recording on and sending off without them.</param>
    /// <param name="handler">What sends to the endpoint; for tests.</param>
    /// <param name="background">Whether a timer writes and sends by itself; tests call <see cref="Flush"/> and <see cref="UploadAsync"/>.</param>
    public Telemetry(string? folder = null, TelemetrySettings? settings = null, HttpMessageHandler? handler = null, bool background = true)
    {
        Folder = folder is null ? null : Path.GetFullPath(folder);
        Settings = settings ?? new TelemetrySettings(true, false, null, "unknown");
        Started = DateTimeOffset.Now;
        SessionId = Guid.NewGuid().ToString("N");
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(10);
        Resource =
        [
            new("service.name", ServiceName),
            new("service.version", Project.Workspaces.AssetConverter.EditorVersion),
            new("service.instance.id", SessionId),
            new("editor.install.id", Settings.InstallId),
            new("os.type", OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "darwin" : "linux"),
            new("os.description", RuntimeInformation.OSDescription),
            new("host.arch", RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()),
            new("process.runtime.description", RuntimeInformation.FrameworkDescription),
        ];
        if (Folder is not null)
        {
            string name = $"{Started:yyyyMMdd-HHmmss}-{SessionId[..8]}";
            LogsFile = Path.Combine(Folder, name + ".logs.jsonl");
            MetricsFile = Path.Combine(Folder, name + ".metrics.jsonl");
            RemoveOldSessions();
            _metrics = new MetricCollector();
            if (background)
            {
                _timer = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
            }
        }
    }

    /// <summary>
    /// The folder with a session's files, the user's own unless <see cref="UserSettings.FileVariable"/>
    /// names another settings file: then the <c>telemetry</c> folder next to it, so that test
    /// editors keep theirs apart.
    /// </summary>
    public static string DefaultFolder => Environment.GetEnvironmentVariable(UserSettings.FileVariable) is { Length: > 0 } settings
        ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(settings))!, "telemetry")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FAF Map Editor", "telemetry");

    public string? Folder { get; }

    public TelemetrySettings Settings { get; private set; }

    public DateTimeOffset Started { get; }

    /// <summary>
    /// The session's id: <c>service.instance.id</c>, and in the names of its files.
    /// </summary>
    public string SessionId { get; }

    public string? LogsFile { get; }

    public string? MetricsFile { get; }

    /// <summary>
    /// What every line and measurement of the session is about: the editor's version, the
    /// session, the install and the operating system.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, object?>> Resource { get; }

    /// <summary>
    /// Whether this session writes to disk now.
    /// </summary>
    public bool IsRecording => Folder is not null && Settings.Record && !_disposed;

    /// <summary>
    /// The least important level logged: information, unless the editor is started with
    /// <c>FAF_MAP_EDITOR_LOG=Debug</c>.
    /// </summary>
    public LogLevel MinimumLevel { get; set; } = Enum.TryParse(Environment.GetEnvironmentVariable("FAF_MAP_EDITOR_LOG"), true, out LogLevel level) ? level : LogLevel.Information;

    public int Warnings => _warnings;

    public int Errors => _errors;

    /// <summary>
    /// Why the last attempt to send failed, or null.
    /// </summary>
    public string? LastUploadError { get; private set; }

    /// <summary>
    /// Raised for every line logged, on the thread that logs it.
    /// </summary>
    public event Action<LogEntry>? Logged;

    public ILogger CreateLogger(string categoryName) => _loggers.GetOrAdd(categoryName, category => new Logger(this, category));

    /// <summary>
    /// Takes the user's new choices. Turning sending on sends only what is recorded from now on.
    /// </summary>
    public void Apply(TelemetrySettings settings)
    {
        if (settings.Send && !Settings.Send)
        {
            Flush();
            MarkAllSent();
        }
        Settings = settings;
    }

    /// <summary>
    /// Writes the logs waiting and the metrics as they are now.
    /// </summary>
    public void Flush()
    {
        WriteLogs();
        WriteMetrics();
    }

    /// <summary>
    /// Sends what was recorded and not sent yet, when sending is on: every session's lines, oldest
    /// first, each to the endpoint's <c>/v1/logs</c> or <c>/v1/metrics</c>. A failure stops it; the
    /// next attempt goes on from there.
    /// </summary>
    /// <returns>The lines sent.</returns>
    public async Task<int> UploadAsync(CancellationToken cancel = default)
    {
        if (Folder is null || !Settings.Send || Settings.Endpoint is not { Length: > 0 } endpoint || !Directory.Exists(Folder)
            || !await _uploading.WaitAsync(0, cancel))
        {
            return 0;
        }
        int sent = 0;
        try
        {
            foreach (string file in Directory.EnumerateFiles(Folder, "*.jsonl").Order(StringComparer.Ordinal))
            {
                string signal = file.EndsWith(".logs.jsonl", StringComparison.Ordinal) ? "logs" : "metrics";
                long offset = SentOffset(file);
                byte[] bytes = ReadFrom(file, offset);
                int start = 0;
                for (int i = 0; i < bytes.Length; i++)
                {
                    if (bytes[i] != '\n')
                    {
                        continue;
                    }
                    using ByteArrayContent content = new ByteArrayContent(bytes, start, i - start);
                    content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                    using HttpResponseMessage response = await _http.PostAsync($"{endpoint.TrimEnd('/')}/v1/{signal}", content, cancel);
                    if (!response.IsSuccessStatusCode)
                    {
                        LastUploadError = $"{endpoint} answered {(int)response.StatusCode} {response.ReasonPhrase}";
                        return sent;
                    }
                    offset += i - start + 1;
                    start = i + 1;
                    File.WriteAllText(file + ".sent", offset.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    sent++;
                }
            }
            LastUploadError = null;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or UriFormatException or InvalidOperationException)
        {
            LastUploadError = exception.Message;
        }
        finally
        {
            _uploading.Release();
        }
        return sent;
    }

    /// <summary>
    /// Writes what is left, and sends it when sending is on (two seconds at most).
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _timer?.Dispose();
        Flush();
        _disposed = true;
        try
        {
            UploadAsync().Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // what is not sent goes with the next session
        }
        _metrics?.Dispose();
    }

    /// <summary>
    /// The text as written: the user's home folder as <c>~</c>.
    /// </summary>
    public string Scrub(string text)
    {
        if (_home.Length < 4)
        {
            return text;
        }
        return text.Replace(_home, "~", StringComparison.OrdinalIgnoreCase)
            .Replace(_home.Replace('\\', '/'), "~", StringComparison.OrdinalIgnoreCase);
    }

    internal void Write(LogEntry entry)
    {
        EditorMetrics.LogRecords.Add(1, EditorMetrics.Tag("level", OtlpJson.SeverityText(entry.Level)), EditorMetrics.Tag("category", entry.Category));
        if (entry.Level >= LogLevel.Error)
        {
            Interlocked.Increment(ref _errors);
        }
        else if (entry.Level == LogLevel.Warning)
        {
            Interlocked.Increment(ref _warnings);
        }
        Logged?.Invoke(entry);
        if (IsRecording && _pending.Count < 10_000)
        {
            _pending.Enqueue(entry);
            if (entry.Level == LogLevel.Critical)
            {
                Flush();
            }
        }
    }

    private void Tick()
    {
        try
        {
            long tick = Interlocked.Increment(ref _ticks);
            WriteLogs();
            if (tick % 30 == 0)
            {
                WriteMetrics();
            }
            if (tick % 15 == 0)
            {
                _ = UploadAsync();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // diagnostics never stop the editor
        }
    }

    private void WriteLogs()
    {
        List<LogEntry> entries = [];
        while (_pending.TryDequeue(out LogEntry? entry))
        {
            entries.Add(entry);
        }
        if (entries.Count > 0 && LogsFile is not null)
        {
            Append(LogsFile, OtlpJson.Logs(Resource, entries, Scrub));
        }
    }

    private void WriteMetrics()
    {
        if (_metrics is not null && MetricsFile is not null && IsRecording)
        {
            IReadOnlyList<MetricCollector.Point> points = _metrics.Snapshot();
            if (points.Count > 0)
            {
                Append(MetricsFile, OtlpJson.Metrics(Resource, points, Started, DateTimeOffset.Now));
            }
        }
    }

    private void Append(string file, byte[] line)
    {
        lock (_fileGate)
        {
            if (!IsRecording)
            {
                return;
            }
            try
            {
                Directory.CreateDirectory(Folder!);
                using FileStream stream = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                if (stream.Length + line.Length > MaxFileBytes)
                {
                    return;
                }
                stream.Write(line);
                stream.WriteByte((byte)'\n');
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // diagnostics never stop the editor
            }
        }
    }

    private void MarkAllSent()
    {
        if (Folder is null || !Directory.Exists(Folder))
        {
            return;
        }
        foreach (string file in Directory.EnumerateFiles(Folder, "*.jsonl"))
        {
            File.WriteAllText(file + ".sent", new FileInfo(file).Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    private static long SentOffset(string file) =>
        File.Exists(file + ".sent") && long.TryParse(File.ReadAllText(file + ".sent"), System.Globalization.CultureInfo.InvariantCulture, out long offset) ? offset : 0;

    private static byte[] ReadFrom(string file, long offset)
    {
        using FileStream stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (offset >= stream.Length)
        {
            return [];
        }
        stream.Position = offset;
        byte[] bytes = new byte[stream.Length - offset];
        stream.ReadExactly(bytes);
        return bytes;
    }

    /// <summary>
    /// Keeps the newest <see cref="KeptSessions"/> sessions, this one among them.
    /// </summary>
    private void RemoveOldSessions()
    {
        if (!Directory.Exists(Folder))
        {
            return;
        }
        try
        {
            IEnumerable<IGrouping<string, string>> old = Directory.EnumerateFiles(Folder)
                .Where(file => file.EndsWith(".jsonl", StringComparison.Ordinal) || file.EndsWith(".jsonl.sent", StringComparison.Ordinal))
                .GroupBy(file => Path.GetFileName(file).Split('.')[0])
                .OrderByDescending(session => session.Key, StringComparer.Ordinal)
                .Skip(KeptSessions - 1);
            foreach (string file in old.SelectMany(session => session))
            {
                File.Delete(file);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // tried again at the next start
        }
    }

    private sealed class Logger(Telemetry telemetry, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= telemetry.MinimumLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }
            string? template = null;
            List<KeyValuePair<string, object?>> attributes = [];
            if (state is IReadOnlyList<KeyValuePair<string, object?>> values)
            {
                foreach (KeyValuePair<string, object?> value in values)
                {
                    if (value.Key == "{OriginalFormat}")
                    {
                        template = value.Value as string;
                    }
                    else
                    {
                        attributes.Add(value);
                    }
                }
            }
            telemetry.Write(new LogEntry(DateTimeOffset.Now, logLevel, category, formatter(state, exception), template, attributes, exception));
        }
    }
}
