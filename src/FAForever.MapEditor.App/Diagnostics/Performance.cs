using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json.Nodes;
using Avalonia.Threading;

namespace FAForever.MapEditor.App.Diagnostics;

/// <summary>
/// Keeps track of where the editor spends its time, to find out why it lags: named spans around
/// the work that may take long (<see cref="Measure"/>), and a watchdog that notices when the user
/// interface's thread does not answer for a while (a stall), with the spans that ran meanwhile.
/// Automation reads it all with <c>perf</c>; stalls of a quarter second or more also go to
/// <see cref="LogFile"/>.
/// </summary>
public static class Performance
{
    /// <summary>
    /// A stall: the user interface's thread answered this much later than it should.
    /// </summary>
    public const double StallMilliseconds = 100;

    private const int Recent = 128;

    private static readonly ConcurrentDictionary<string, Statistic> Statistics = new ConcurrentDictionary<string, Statistic>(StringComparer.Ordinal);
    private static readonly ConcurrentQueue<(DateTime At, double Milliseconds, string Spans)> Stalls = new();
    private static readonly ConcurrentQueue<(long End, string Name, double Milliseconds)> Finished = new();
    private static Thread? _watchdog;

    /// <summary>
    /// Where the long stalls are written, next to the user's settings.
    /// </summary>
    public static string LogFile { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FAF Map Editor", "performance.log");

    /// <summary>
    /// Measures until the result is disposed: <c>using (Performance.Measure("3d.frame")) { ... }</c>.
    /// </summary>
    public static Span Measure(string name) => new Span(name, Stopwatch.GetTimestamp());

    public readonly struct Span(string name, long start) : IDisposable
    {
        public void Dispose()
        {
            long end = Stopwatch.GetTimestamp();
            double milliseconds = Stopwatch.GetElapsedTime(start, end).TotalMilliseconds;
            Statistics.GetOrAdd(name, _ => new Statistic()).Add(milliseconds);
            if (milliseconds >= 1)
            {
                Finished.Enqueue((end, name, milliseconds));
                while (Finished.Count > 4096 && Finished.TryDequeue(out _))
                {
                }
            }
        }
    }

    /// <summary>
    /// Starts the watchdog: every 50 ms it asks the user interface's thread to answer, and counts
    /// how late the answer comes.
    /// </summary>
    public static void StartWatchdog()
    {
        if (_watchdog is not null)
        {
            return;
        }
        _watchdog = new Thread(() =>
        {
            while (true)
            {
                Thread.Sleep(50);
                long asked = Stopwatch.GetTimestamp();
                using ManualResetEventSlim answered = new ManualResetEventSlim();
                Dispatcher.UIThread.Post(() => answered.Set(), DispatcherPriority.Input);
                answered.Wait();
                long now = Stopwatch.GetTimestamp();
                double late = Stopwatch.GetElapsedTime(asked, now).TotalMilliseconds;
                Statistics.GetOrAdd("ui.latency", _ => new Statistic()).Add(late);
                if (late >= StallMilliseconds)
                {
                    Stall(asked, now, late);
                }
            }
        })
        {
            IsBackground = true,
            Name = "Performance watchdog",
        };
        _watchdog.Start();
    }

    private static void Stall(long from, long to, double milliseconds)
    {
        // what ran while the thread did not answer, the longest first
        string spans = string.Join(", ", Finished
            .Where(span => span.End >= from && span.End <= to)
            .GroupBy(span => span.Name)
            .Select(group => (Name: group.Key, Count: group.Count(), Total: group.Sum(span => span.Milliseconds)))
            .OrderByDescending(group => group.Total)
            .Take(8)
            .Select(group => $"{group.Name} {group.Total:0} ms ({group.Count}x)"));
        Stalls.Enqueue((DateTime.Now, milliseconds, spans));
        while (Stalls.Count > 100 && Stalls.TryDequeue(out _))
        {
        }
        if (milliseconds >= 250)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogFile)!);
                File.AppendAllText(LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} stall {milliseconds:0} ms: {(spans.Length > 0 ? spans : "no measured work")}\n");
            }
            catch (IOException)
            {
                // the log is a convenience
            }
        }
    }

    public static void Reset()
    {
        Statistics.Clear();
        Stalls.Clear();
        Finished.Clear();
    }

    /// <summary>
    /// Per span: how often, the total, mean, 95th percentile of the recent ones and the longest, in
    /// milliseconds, the most time first; and the recent stalls.
    /// </summary>
    public static JsonObject Snapshot()
    {
        JsonObject spans = new JsonObject();
        foreach ((string name, Statistic statistic) in Statistics.OrderByDescending(pair => pair.Value.Total))
        {
            spans[name] = statistic.ToJson();
        }
        return new JsonObject
        {
            ["spans"] = spans,
            ["stalls"] = new JsonArray([.. Stalls.Reverse().Take(20).Select(stall => (JsonNode)new JsonObject
            {
                ["at"] = stall.At.ToString("HH:mm:ss.fff"),
                ["ms"] = Math.Round(stall.Milliseconds),
                ["during"] = stall.Spans,
            })]),
            ["log"] = LogFile,
        };
    }

    private sealed class Statistic
    {
        private readonly object _gate = new object();
        private readonly double[] _recent = new double[Recent];
        private long _count;
        private double _total;
        private double _max;

        public double Total
        {
            get
            {
                lock (_gate)
                {
                    return _total;
                }
            }
        }

        public void Add(double milliseconds)
        {
            lock (_gate)
            {
                _recent[_count % Recent] = milliseconds;
                _count++;
                _total += milliseconds;
                _max = Math.Max(_max, milliseconds);
            }
        }

        public JsonObject ToJson()
        {
            lock (_gate)
            {
                double[] recent = [.. _recent.Take((int)Math.Min(_count, Recent)).Order()];
                return new JsonObject
                {
                    ["count"] = _count,
                    ["totalMs"] = Math.Round(_total, 1),
                    ["meanMs"] = Math.Round(_total / Math.Max(1, _count), 2),
                    ["p95Ms"] = recent.Length == 0 ? 0 : Math.Round(recent[(int)(recent.Length * 0.95)], 2),
                    ["maxMs"] = Math.Round(_max, 1),
                };
            }
        }
    }
}
