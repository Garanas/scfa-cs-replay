using System.Globalization;
using System.Text;
using StatBin = FAForever.Vault.Viewer.Services.Players.Statistics.HistogramBin;
using FAForever.Vault.Viewer.Features.Players.Charts;
using FAForever.Vault.Viewer.Services.Players;
using FAForever.Vault.Viewer.Services.Players.Statistics;

namespace FAForever.Vault.Viewer.Features.Players.Profile;

/// <summary>What the histogram of the change per game says in its legend.</summary>
public sealed record ChangeSplit(int Losses, double? MeanLoss, int Wins, double? MeanWin);

/// <summary>Pure shaping of a rating journal into what the charts, the table and the CSV need.</summary>
public static class RatingChartData
{
    /// <summary>The rows of the journal table per page.</summary>
    public const int PageSize = 25;

    /// <summary>Longest list a sparkline gets; more points are thinned out.</summary>
    public const int SparklinePoints = 80;

    /// <summary>
    /// Where to start loading: the start of the period, or 30 days back when that is earlier, so
    /// the change over 30 days is known for a short period too. Null for all time.
    /// </summary>
    public static DateTimeOffset? LoadSince(DateTimeOffset? periodStart, DateTimeOffset now)
        => periodStart is null ? null : periodStart < now.AddDays(-30) ? periodStart : now.AddDays(-30);

    /// <summary>The entries that fall at or after a moment (all of them for null), oldest first.</summary>
    public static IReadOnlyList<RatingJournalEntry> Within(IReadOnlyList<RatingJournalEntry> entries, DateTimeOffset? since)
        => [.. Ordered(entries).Where(entry => since is null || entry.Date >= since)];

    /// <summary>The rating line: one point per game, the entry itself as the point's data (for the tooltip).</summary>
    public static IReadOnlyList<LinePoint> Points(IReadOnlyList<RatingJournalEntry> entries)
        => [.. Ordered(entries).Where(entry => entry.Date is not null).Select(entry => new LinePoint(entry.Date!.Value, entry.RatingAfter, entry))];

    /// <summary>The uncertainty σ after each game, on the same times as <see cref="Points"/>.</summary>
    public static IReadOnlyList<LinePoint> Sigma(IReadOnlyList<RatingJournalEntry> entries)
        => [.. Ordered(entries).Where(entry => entry.Date is not null).Select(entry => new LinePoint(entry.Date!.Value, entry.DeviationAfter, entry))];

    /// <summary>The highest point (the first one when several are equal); null without points.</summary>
    public static LinePoint? Peak(IReadOnlyList<LinePoint> points)
    {
        LinePoint? peak = null;
        foreach (LinePoint point in points)
        {
            if (peak is null || point.Value > peak.Value)
            {
                peak = point;
            }
        }

        return peak;
    }

    /// <summary>The game that raised the rating the most; null when none raised it.</summary>
    public static RatingJournalEntry? BiggestGain(IReadOnlyList<RatingJournalEntry> entries)
        => Ordered(entries).Where(entry => entry.Change > 0).OrderByDescending(entry => entry.Change).FirstOrDefault();

    /// <summary>The game that lowered the rating the most; null when none lowered it.</summary>
    public static RatingJournalEntry? BiggestLoss(IReadOnlyList<RatingJournalEntry> entries)
        => Ordered(entries).Where(entry => entry.Change < 0).OrderBy(entry => entry.Change).FirstOrDefault();

    /// <summary>The games that lowered and raised the rating, with the mean change of each.</summary>
    public static ChangeSplit Split(IReadOnlyList<RatingJournalEntry> entries)
    {
        double[] losses = [.. entries.Select(entry => entry.Change).Where(change => change < 0)];
        double[] wins = [.. entries.Select(entry => entry.Change).Where(change => change > 0)];
        return new ChangeSplit(
            losses.Length,
            losses.Length > 0 ? losses.Average() : null,
            wins.Length,
            wins.Length > 0 ? wins.Average() : null);
    }

    /// <summary>
    /// The histogram of the change per game: a bin that ends at or below zero is a loss, any other a
    /// win. Every second edge is labelled; the tallest bin carries its count.
    /// </summary>
    public static IReadOnlyList<Charts.HistogramBin> ChangeBins(IReadOnlyList<RatingJournalEntry> entries)
    {
        IReadOnlyList<StatBin> bins = RatingStatistics.ChangeHistogram(entries);
        int tallest = bins.Count == 0 ? 0 : bins.Max(bin => bin.Count);
        List<Charts.HistogramBin> result = [];
        for (int i = 0; i < bins.Count; i++)
        {
            StatBin bin = bins[i];
            string label = i % 2 == 1 ? EdgeLabel(bin.From) : "";
            string? top = bin.Count > 0 && bin.Count == tallest ? bin.Count.ToString("N0", CultureInfo.InvariantCulture) : null;
            string range = i == 0
                ? $"{ProfileFormat.Minus}{Math.Abs(bin.To):0} or lower"
                : i == bins.Count - 1 ? $"{bin.From:0} or higher" : $"{EdgeLabel(bin.From)} to {EdgeLabel(bin.To)}";
            result.Add(new Charts.HistogramBin(label, bin.Count, bin.To <= 0 ? -1 : 1, top, $"{bin.Count:N0} games, change {range}"));
        }

        return result;
    }

    private static string EdgeLabel(double edge)
        => edge > 0 ? "+" + edge.ToString("0", CultureInfo.InvariantCulture)
            : edge < 0 ? ProfileFormat.Minus + Math.Abs(edge).ToString("0", CultureInfo.InvariantCulture) : "0";

    /// <summary>The values of a sparkline: the rating after each game, thinned to at most <paramref name="max"/> values (the last one is kept).</summary>
    public static IReadOnlyList<double> SparkValues(IReadOnlyList<RatingJournalEntry> entries, int max = SparklinePoints)
    {
        double[] values = [.. Ordered(entries).Select(entry => entry.RatingAfter)];
        if (values.Length <= max || max < 2)
        {
            return values;
        }

        double[] thinned = new double[max];
        for (int i = 0; i < max; i++)
        {
            thinned[i] = values[(int)Math.Round(i * (values.Length - 1) / (double)(max - 1))];
        }

        return thinned;
    }

    /// <summary>The page of the table (newest first), from 1; a page past the end is the last one.</summary>
    public static IReadOnlyList<RatingJournalEntry> TablePage(IReadOnlyList<RatingJournalEntry> entries, int page, out int pageCount, out int shownPage)
    {
        pageCount = Math.Max(1, (entries.Count + PageSize - 1) / PageSize);
        shownPage = Math.Clamp(page, 1, pageCount);
        return [.. Ordered(entries).Reverse().Skip((shownPage - 1) * PageSize).Take(PageSize)];
    }

    /// <summary>
    /// The journal as CSV text, newest first, one row per game: ISO date in UTC, game, map,
    /// opponents, result word, the rating before and after, the change, mean and deviation after.
    /// Fields with a comma or a quote are quoted.
    /// </summary>
    public static string Csv(IReadOnlyList<RatingJournalEntry> entries)
    {
        StringBuilder csv = new();
        csv.Append("Date,Game,Map,Opponents,Result,Rating before,Rating after,Change,Mean after,Deviation after\r\n");
        foreach (RatingJournalEntry entry in Ordered(entries).Reverse())
        {
            csv.Append(Field(entry.Date?.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))).Append(',')
                .Append(entry.GameId.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(Field(entry.MapName)).Append(',')
                .Append(Field(string.Join("; ", entry.Opponents.Select(opponent => opponent.Login)))).Append(',')
                .Append(ProfileFormat.ResultWord(entry.Change)).Append(',')
                .Append(Math.Round(entry.RatingBefore, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture)).Append(',')
                .Append(Math.Round(entry.RatingAfter, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture)).Append(',')
                .Append(Math.Round(entry.Change, 1, MidpointRounding.AwayFromZero).ToString("0.0", CultureInfo.InvariantCulture)).Append(',')
                .Append(entry.MeanAfter.ToString("0.0", CultureInfo.InvariantCulture)).Append(',')
                .Append(entry.DeviationAfter.ToString("0.0", CultureInfo.InvariantCulture)).Append("\r\n");
        }

        return csv.ToString();
    }

    /// <summary>The CSV as a data address for a download link (with a byte order mark so a spreadsheet reads UTF-8).</summary>
    public static string CsvDataUrl(string csv) => "data:text/csv;charset=utf-8,%EF%BB%BF" + Uri.EscapeDataString(csv);

    private static string Field(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        return text.AsSpan().IndexOfAny(",\"\r\n") >= 0 ? "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : text;
    }

    private static RatingJournalEntry[] Ordered(IReadOnlyList<RatingJournalEntry> entries)
        => [.. entries.OrderBy(entry => entry.Date ?? DateTimeOffset.MinValue).ThenBy(entry => entry.GameId)];
}
