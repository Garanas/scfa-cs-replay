using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FAForever.MapEditor.App.Diagnostics;

/// <summary>
/// Keeps track of where the editor spends its time, to find out why it lags: named spans around
/// the work that may take long (<see cref="Measure"/>), and a watchdog that notices when the user
/// interface's thread does not answer for a while (a stall), with the spans that ran meanwhile.
/// Automation reads it all with <c>perf</c>. The spans and stalls are metrics too
/// (<see cref="EditorMetrics"/>), and a stall of a quarter second or more is a warning in the log
/// (<see cref="Logger"/>).
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
    /// Where the long stalls are logged, in the category <c>Performance</c>.
    /// </summary>
    public static ILogger Logger { get; set; } = NullLogger.Instance;

    /// <summary>
    /// Measures until the result is disposed: <c>using (Performance.Measure("3d.frame")) { ... }</c>.
    /// </summary>
    public static Span Measure(string name) => new Span(name, Stopwatch.GetTimestamp());

    public readonly struct Span(string name, long start) : IDisposable
    {
        public void Dispose() => Record(name, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }

    /// <summary>
    /// Records a span measured elsewhere (the graphics card's time for a frame), as if it ended now.
    /// </summary>
    public static void Record(string name, double milliseconds)
    {
        long end = Stopwatch.GetTimestamp();
        Statistics.GetOrAdd(name, _ => new Statistic()).Add(milliseconds);
        EditorMetrics.SpanDuration.Record(milliseconds, EditorMetrics.Tag("span", name));
        if (milliseconds >= 1)
        {
            Finished.Enqueue((end, name, milliseconds));
            while (Finished.Count > 4096 && Finished.TryDequeue(out _))
            {
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
                EditorMetrics.UiLatency.Record(late);
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
        EditorMetrics.UiStalls.Add(1);
        Stalls.Enqueue((DateTime.Now, milliseconds, spans));
        while (Stalls.Count > 100 && Stalls.TryDequeue(out _))
        {
        }
        if (milliseconds >= 250)
        {
            Logger.LogWarning("The window did not answer for {Milliseconds} ms; meanwhile {Spans}",
                Math.Round(milliseconds), spans.Length > 0 ? spans : "no measured work");
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
