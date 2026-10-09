using System.Diagnostics.Metrics;
using System.Text;

namespace FAForever.MapEditor.App.Diagnostics;

/// <summary>
/// Adds up the measurements of <see cref="EditorMetrics"/> since the session started, per
/// instrument and set of attributes: a counter's sum, a histogram's buckets, a gauge's last value.
/// </summary>
public sealed class MetricCollector : IDisposable
{
    /// <summary>
    /// The upper bounds of a histogram's buckets, in milliseconds: from a frame at 60 per second
    /// to a conversion of the game's assets.
    /// </summary>
    public static readonly double[] Bounds = [0.5, 1, 2, 4, 8, 16, 33, 50, 100, 250, 500, 1000, 2500, 5000, 10_000, 30_000, 60_000, 120_000];

    public enum Kind
    {
        Sum,
        Histogram,
        Gauge,
    }

    /// <summary>
    /// An instrument with one set of attributes.
    /// </summary>
    public sealed class Point(Instrument instrument, Kind kind, KeyValuePair<string, object?>[] attributes)
    {
        public Instrument Instrument => instrument;

        public Kind Kind => kind;

        public KeyValuePair<string, object?>[] Attributes => attributes;

        public bool IsInteger { get; init; }

        /// <summary>
        /// Whether no snapshot held it before: its series starts in this one.
        /// </summary>
        public bool IsNew { get; set; }

        public long Count { get; set; }

        public double Sum { get; set; }

        public double Min { get; set; } = double.PositiveInfinity;

        public double Max { get; set; } = double.NegativeInfinity;

        public double Last { get; set; }

        public long[] Buckets { get; } = kind == Kind.Histogram ? new long[Bounds.Length + 1] : [];

        public Point Copy()
        {
            Point copy = new Point(instrument, kind, attributes)
            {
                IsInteger = IsInteger,
                IsNew = IsNew,
                Count = Count,
                Sum = Sum,
                Min = Min,
                Max = Max,
                Last = Last,
            };
            Buckets.CopyTo(copy.Buckets, 0);
            return copy;
        }
    }

    private readonly MeterListener _listener = new MeterListener();
    private readonly object _gate = new object();
    private readonly Dictionary<(Instrument, string), Point> _points = [];

    public MetricCollector()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == EditorMetrics.MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags, true));
        _listener.SetMeasurementEventCallback<int>((instrument, value, tags, _) => Record(instrument, value, tags, true));
        _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags, false));
        _listener.Start();
    }

    /// <summary>
    /// Every instrument with its attributes as it is now, the gauges read first.
    /// </summary>
    public IReadOnlyList<Point> Snapshot()
    {
        _listener.RecordObservableInstruments();
        lock (_gate)
        {
            Point[] points = [.. _points.Values.Select(point => point.Copy())];
            foreach (Point point in _points.Values)
            {
                point.IsNew = false;
            }
            return points;
        }
    }

    public void Dispose() => _listener.Dispose();

    private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags, bool integer)
    {
        string key = Key(tags);
        lock (_gate)
        {
            if (!_points.TryGetValue((instrument, key), out Point? point))
            {
                point = new Point(instrument, KindOf(instrument), tags.ToArray()) { IsInteger = integer, IsNew = true };
                _points[(instrument, key)] = point;
            }
            point.Count++;
            point.Sum += value;
            point.Last = value;
            point.Min = Math.Min(point.Min, value);
            point.Max = Math.Max(point.Max, value);
            if (point.Kind == Kind.Histogram)
            {
                // bucket i holds the values above bound i - 1 up to bound i
                int bucket = 0;
                while (bucket < Bounds.Length && value > Bounds[bucket])
                {
                    bucket++;
                }
                point.Buckets[bucket]++;
            }
        }
    }

    private static Kind KindOf(Instrument instrument)
    {
        Type type = instrument.GetType().IsGenericType ? instrument.GetType().GetGenericTypeDefinition() : instrument.GetType();
        return type == typeof(Histogram<>) ? Kind.Histogram
            : type == typeof(ObservableGauge<>) || type == typeof(Gauge<>) ? Kind.Gauge
            : Kind.Sum;
    }

    private static string Key(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        if (tags.Length == 0)
        {
            return "";
        }
        if (tags.Length == 1)
        {
            return $"{tags[0].Key}={tags[0].Value}";
        }
        StringBuilder key = new StringBuilder();
        foreach (KeyValuePair<string, object?> tag in tags)
        {
            key.Append(tag.Key).Append('=').Append(tag.Value).Append('\n');
        }
        return key.ToString();
    }
}
