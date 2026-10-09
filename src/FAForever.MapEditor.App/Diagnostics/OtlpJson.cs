using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.Diagnostics;

/// <summary>
/// OpenTelemetry's JSON (OTLP/JSON, <c>opentelemetry-proto</c>'s <c>ExportLogsServiceRequest</c> and
/// <c>ExportMetricsServiceRequest</c>): what a collector takes on <c>/v1/logs</c> and
/// <c>/v1/metrics</c>, and reads from files with its <c>otlpjsonfile</c> receiver. 64-bit numbers
/// are strings, as the format wants.
/// </summary>
internal static class OtlpJson
{
    private static readonly JsonWriterOptions Options = new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    /// The logs as one request, a scope per category.
    /// </summary>
    public static byte[] Logs(IReadOnlyList<KeyValuePair<string, object?>> resource, IReadOnlyList<LogEntry> entries, Func<string, string> scrub)
    {
        using MemoryStream stream = new MemoryStream();
        using (Utf8JsonWriter json = new Utf8JsonWriter(stream, Options))
        {
            json.WriteStartObject();
            json.WriteStartArray("resourceLogs");
            json.WriteStartObject();
            WriteResource(json, resource);
            json.WriteStartArray("scopeLogs");
            foreach (IGrouping<string, LogEntry> category in entries.GroupBy(entry => entry.Category))
            {
                json.WriteStartObject();
                WriteScope(json, $"{EditorMetrics.MeterName}.{category.Key}");
                json.WriteStartArray("logRecords");
                foreach (LogEntry entry in category)
                {
                    json.WriteStartObject();
                    json.WriteString("timeUnixNano", Nanoseconds(entry.Time));
                    json.WriteString("observedTimeUnixNano", Nanoseconds(entry.Time));
                    json.WriteNumber("severityNumber", SeverityNumber(entry.Level));
                    json.WriteString("severityText", SeverityText(entry.Level));
                    json.WriteStartObject("body");
                    json.WriteString("stringValue", scrub(entry.Message));
                    json.WriteEndObject();
                    List<KeyValuePair<string, object?>> attributes = [.. entry.Attributes];
                    if (entry.Template is not null)
                    {
                        attributes.Add(new("message.template", entry.Template));
                    }
                    if (entry.Exception is Exception exception)
                    {
                        attributes.Add(new("exception.type", exception.GetType().FullName));
                        attributes.Add(new("exception.message", exception.Message));
                        attributes.Add(new("exception.stacktrace", exception.ToString()));
                    }
                    WriteAttributes(json, attributes, scrub);
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
            json.WriteEndArray();
            json.WriteEndObject();
        }
        return stream.ToArray();
    }

    /// <summary>
    /// The metrics as one request: sums and histograms cumulative since <paramref name="start"/>.
    /// A series that is new starts with a zero at <paramref name="start"/>: Prometheus's
    /// <c>increase</c> counts from a series' first sample, and would miss an error that happened
    /// once in a session.
    /// </summary>
    public static byte[] Metrics(IReadOnlyList<KeyValuePair<string, object?>> resource, IReadOnlyList<MetricCollector.Point> points, DateTimeOffset start, DateTimeOffset now)
    {
        string from = Nanoseconds(start);
        string to = Nanoseconds(now);
        using MemoryStream stream = new MemoryStream();
        using (Utf8JsonWriter json = new Utf8JsonWriter(stream, Options))
        {
            json.WriteStartObject();
            json.WriteStartArray("resourceMetrics");
            json.WriteStartObject();
            WriteResource(json, resource);
            json.WriteStartArray("scopeMetrics");
            json.WriteStartObject();
            WriteScope(json, EditorMetrics.MeterName);
            json.WriteStartArray("metrics");
            foreach (IGrouping<System.Diagnostics.Metrics.Instrument, MetricCollector.Point> instrument in points.GroupBy(point => point.Instrument).OrderBy(group => group.Key.Name, StringComparer.Ordinal))
            {
                MetricCollector.Kind kind = instrument.First().Kind;
                json.WriteStartObject();
                json.WriteString("name", instrument.Key.Name);
                json.WriteString("description", instrument.Key.Description ?? "");
                json.WriteString("unit", instrument.Key.Unit ?? "");
                json.WriteStartObject(kind switch
                {
                    MetricCollector.Kind.Histogram => "histogram",
                    MetricCollector.Kind.Gauge => "gauge",
                    _ => "sum",
                });
                if (kind != MetricCollector.Kind.Gauge)
                {
                    // cumulative
                    json.WriteNumber("aggregationTemporality", 2);
                }
                if (kind == MetricCollector.Kind.Sum)
                {
                    json.WriteBoolean("isMonotonic", true);
                }
                json.WriteStartArray("dataPoints");
                foreach (MetricCollector.Point point in instrument)
                {
                    if (point.IsNew && kind != MetricCollector.Kind.Gauge)
                    {
                        WriteZero(json, point, kind, from);
                    }
                    json.WriteStartObject();
                    WriteAttributes(json, point.Attributes, text => text);
                    if (kind != MetricCollector.Kind.Gauge)
                    {
                        json.WriteString("startTimeUnixNano", from);
                    }
                    json.WriteString("timeUnixNano", to);
                    switch (kind)
                    {
                        case MetricCollector.Kind.Histogram:
                            json.WriteString("count", point.Count.ToString(CultureInfo.InvariantCulture));
                            json.WriteNumber("sum", point.Sum);
                            json.WriteStartArray("bucketCounts");
                            foreach (long count in point.Buckets)
                            {
                                json.WriteStringValue(count.ToString(CultureInfo.InvariantCulture));
                            }
                            json.WriteEndArray();
                            json.WriteStartArray("explicitBounds");
                            foreach (double bound in MetricCollector.Bounds)
                            {
                                json.WriteNumberValue(bound);
                            }
                            json.WriteEndArray();
                            json.WriteNumber("min", point.Min);
                            json.WriteNumber("max", point.Max);
                            break;
                        default:
                            double value = kind == MetricCollector.Kind.Gauge ? point.Last : point.Sum;
                            if (point.IsInteger)
                            {
                                json.WriteString("asInt", ((long)value).ToString(CultureInfo.InvariantCulture));
                            }
                            else
                            {
                                json.WriteNumber("asDouble", value);
                            }
                            break;
                    }
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteEndObject();
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
            json.WriteEndArray();
            json.WriteEndObject();
            json.WriteEndArray();
            json.WriteEndObject();
        }
        return stream.ToArray();
    }

    private static void WriteZero(Utf8JsonWriter json, MetricCollector.Point point, MetricCollector.Kind kind, string start)
    {
        json.WriteStartObject();
        WriteAttributes(json, point.Attributes, text => text);
        json.WriteString("startTimeUnixNano", start);
        json.WriteString("timeUnixNano", start);
        if (kind == MetricCollector.Kind.Histogram)
        {
            json.WriteString("count", "0");
            json.WriteNumber("sum", 0);
            json.WriteStartArray("bucketCounts");
            for (int i = 0; i <= MetricCollector.Bounds.Length; i++)
            {
                json.WriteStringValue("0");
            }
            json.WriteEndArray();
            json.WriteStartArray("explicitBounds");
            foreach (double bound in MetricCollector.Bounds)
            {
                json.WriteNumberValue(bound);
            }
            json.WriteEndArray();
        }
        else if (point.IsInteger)
        {
            json.WriteString("asInt", "0");
        }
        else
        {
            json.WriteNumber("asDouble", 0);
        }
        json.WriteEndObject();
    }

    /// <summary>
    /// The level as OpenTelemetry names it (<c>severityText</c>), and as Loki's <c>detected_level</c> reads it.
    /// </summary>
    public static string SeverityText(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRACE",
        LogLevel.Debug => "DEBUG",
        LogLevel.Information => "INFO",
        LogLevel.Warning => "WARN",
        LogLevel.Error => "ERROR",
        _ => "FATAL",
    };

    private static int SeverityNumber(LogLevel level) => level switch
    {
        LogLevel.Trace => 1,
        LogLevel.Debug => 5,
        LogLevel.Information => 9,
        LogLevel.Warning => 13,
        LogLevel.Error => 17,
        _ => 21,
    };

    private static string Nanoseconds(DateTimeOffset time) =>
        ((time.ToUnixTimeMilliseconds() * 1_000_000L) + (time.Ticks % TimeSpan.TicksPerMillisecond * 100)).ToString(CultureInfo.InvariantCulture);

    private static void WriteResource(Utf8JsonWriter json, IReadOnlyList<KeyValuePair<string, object?>> resource)
    {
        json.WriteStartObject("resource");
        WriteAttributes(json, resource, text => text);
        json.WriteEndObject();
    }

    private static void WriteScope(Utf8JsonWriter json, string name)
    {
        json.WriteStartObject("scope");
        json.WriteString("name", name);
        json.WriteString("version", Project.Workspaces.AssetConverter.EditorVersion);
        json.WriteEndObject();
    }

    private static void WriteAttributes(Utf8JsonWriter json, IEnumerable<KeyValuePair<string, object?>> attributes, Func<string, string> scrub)
    {
        json.WriteStartArray("attributes");
        foreach ((string key, object? value) in attributes)
        {
            if (value is null)
            {
                continue;
            }
            json.WriteStartObject();
            json.WriteString("key", key);
            json.WriteStartObject("value");
            switch (value)
            {
                case bool flag:
                    json.WriteBoolean("boolValue", flag);
                    break;
                case int or long or short or byte or uint:
                    json.WriteString("intValue", Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
                    break;
                case double or float or decimal:
                    json.WriteNumber("doubleValue", Convert.ToDouble(value, CultureInfo.InvariantCulture));
                    break;
                default:
                    json.WriteString("stringValue", scrub(Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""));
                    break;
            }
            json.WriteEndObject();
            json.WriteEndObject();
        }
        json.WriteEndArray();
    }
}
