using System.Diagnostics.Metrics;

namespace FAForever.MapEditor.App.Diagnostics;

/// <summary>
/// What the editor measures about itself, as the metrics of a service would be: counters and
/// histograms with a few attributes of known values (a command's id, a span's name), never a path
/// or a map's name. <see cref="Telemetry"/> collects them per session; in Prometheus they become
/// <c>editor_commands_total</c>, <c>editor_span_duration_milliseconds_bucket</c>, and so on.
/// </summary>
public static class EditorMetrics
{
    public const string MeterName = "FAForever.MapEditor";

    public static readonly Meter Meter = new Meter(MeterName);

    /// <summary>
    /// The commands run, by <c>command</c> and <c>result</c>: <c>ran</c>, <c>not_built</c> (asked
    /// for while not built yet), <c>cannot_run</c> or <c>failed</c>.
    /// </summary>
    public static readonly Counter<long> Commands = Meter.CreateCounter<long>(
        "editor.commands", "{command}", "Commands of the controller run, by command and result");

    /// <summary>
    /// The lines logged, by <c>level</c> and <c>category</c>: how often things go wrong.
    /// </summary>
    public static readonly Counter<long> LogRecords = Meter.CreateCounter<long>(
        "editor.log.records", "{record}", "Lines logged, by level and category");

    /// <summary>
    /// The measured spans of <see cref="Performance"/>, by <c>span</c>.
    /// </summary>
    public static readonly Histogram<double> SpanDuration = Meter.CreateHistogram<double>(
        "editor.span.duration", "ms", "How long measured work took, by span");

    /// <summary>
    /// How late the user interface's thread answers the watchdog.
    /// </summary>
    public static readonly Histogram<double> UiLatency = Meter.CreateHistogram<double>(
        "editor.ui.latency", "ms", "How late the window answers");

    /// <summary>
    /// The times the window did not answer for <see cref="Performance.StallMilliseconds"/> or more.
    /// </summary>
    public static readonly Counter<long> UiStalls = Meter.CreateCounter<long>(
        "editor.ui.stalls", "{stall}", "Times the window did not answer for 100 ms or more");

    /// <summary>
    /// Opening, importing, saving, exporting and converting the game's assets, by <c>operation</c>
    /// and <c>result</c> (<c>ok</c> or <c>failed</c>).
    /// </summary>
    public static readonly Histogram<double> OperationDuration = Meter.CreateHistogram<double>(
        "editor.operation.duration", "ms", "How long operations on files took, by operation and result");

    /// <summary>
    /// The memory the editor's process uses.
    /// </summary>
    public static readonly ObservableGauge<long> WorkingSet = Meter.CreateObservableGauge(
        "editor.memory.working_set", () => Environment.WorkingSet, "By", "Memory of the process");

    /// <summary>
    /// The memory .NET's garbage collector holds.
    /// </summary>
    public static readonly ObservableGauge<long> ManagedMemory = Meter.CreateObservableGauge(
        "editor.memory.managed", () => GC.GetTotalMemory(false), "By", "Memory the garbage collector holds");

    /// <summary>
    /// One attribute, for <c>Add</c> and <c>Record</c>.
    /// </summary>
    public static KeyValuePair<string, object?> Tag(string key, object? value) => new KeyValuePair<string, object?>(key, value);
}
