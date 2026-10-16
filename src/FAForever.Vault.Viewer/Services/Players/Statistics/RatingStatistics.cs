namespace FAForever.Vault.Viewer.Services.Players.Statistics;

/// <summary>The numbers on top of the rating history.</summary>
/// <param name="Current">The display rating after the latest game.</param>
/// <param name="Peak">The highest display rating after any game.</param>
/// <param name="PeakDate">The date of the game that reached the peak.</param>
/// <param name="Change30Days">The rating now minus the rating before the first game of the last 30 days; null without a game in them.</param>
/// <param name="Games">The number of rated games.</param>
/// <param name="Wins">Games that raised the rating.</param>
/// <param name="Losses">Games that lowered the rating (a rating that did not move is neither).</param>
/// <param name="AverageChange">The mean change per game.</param>
/// <param name="BiggestGain">The largest rise in one game; null when no game raised the rating.</param>
/// <param name="BiggestLoss">The largest fall in one game, a negative number; null when no game lowered the rating.</param>
public sealed record RatingSummary(
    int? Current,
    int? Peak,
    DateTimeOffset? PeakDate,
    int? Change30Days,
    int Games,
    int Wins,
    int Losses,
    double? AverageChange,
    double? BiggestGain,
    double? BiggestLoss);

/// <summary>A point of the rating line.</summary>
public sealed record RatingPoint(DateTimeOffset Date, int GameId, double Rating, double Mean, double Deviation);

/// <summary>A bin of a histogram: <c>From</c> inclusive, <c>To</c> exclusive (the last bin includes <c>To</c>).</summary>
public sealed record HistogramBin(double From, double To, int Count);

/// <summary>Statistics over a player's rating journal on one board.</summary>
public static class RatingStatistics
{
    /// <summary>The width of a bin of the change histogram.</summary>
    public const int HistogramBinWidth = 4;

    /// <summary>The lowest edge of the change histogram.</summary>
    public const int HistogramMin = -28;

    /// <summary>The highest edge of the change histogram.</summary>
    public const int HistogramMax = 28;

    /// <summary>The summary of the entries (in any order) as of <paramref name="now"/>.</summary>
    public static RatingSummary Summarize(IReadOnlyList<RatingJournalEntry> entries, DateTimeOffset now)
    {
        if (entries.Count == 0)
        {
            return new RatingSummary(null, null, null, null, 0, 0, 0, null, null, null);
        }

        RatingJournalEntry[] ordered = Ordered(entries);
        int current = Display(ordered[^1].MeanAfter, ordered[^1].DeviationAfter);

        RatingJournalEntry peakEntry = ordered[0];
        foreach (RatingJournalEntry entry in ordered)
        {
            if (entry.RatingAfter > peakEntry.RatingAfter)
            {
                peakEntry = entry;
            }
        }

        DateTimeOffset since = now.AddDays(-30);
        RatingJournalEntry? firstRecent = ordered.FirstOrDefault(entry => entry.Date is { } date && date >= since);
        int? change30 = firstRecent is null ? null : current - Display(firstRecent.MeanBefore, firstRecent.DeviationBefore);

        double[] changes = [.. ordered.Select(entry => entry.Change)];
        return new RatingSummary(
            current,
            Display(peakEntry.MeanAfter, peakEntry.DeviationAfter),
            peakEntry.Date,
            change30,
            ordered.Length,
            changes.Count(change => change > 0),
            changes.Count(change => change < 0),
            changes.Average(),
            changes.Any(change => change > 0) ? changes.Max() : null,
            changes.Any(change => change < 0) ? changes.Min() : null);
    }

    /// <summary>
    /// The change per game in bins of 4 from -28 to +28 (14 bins); a larger change goes in the
    /// first or the last bin.
    /// </summary>
    public static IReadOnlyList<HistogramBin> ChangeHistogram(IReadOnlyList<RatingJournalEntry> entries)
    {
        int binCount = (HistogramMax - HistogramMin) / HistogramBinWidth;
        int[] counts = new int[binCount];
        foreach (RatingJournalEntry entry in entries)
        {
            int bin = (int)Math.Floor((entry.Change - HistogramMin) / HistogramBinWidth);
            counts[Math.Clamp(bin, 0, binCount - 1)]++;
        }

        return [.. counts.Select((count, index) => new HistogramBin(
            HistogramMin + index * HistogramBinWidth,
            HistogramMin + (index + 1) * HistogramBinWidth,
            count))];
    }

    /// <summary>The rating line, oldest first; games without a date are left out.</summary>
    public static IReadOnlyList<RatingPoint> Series(IReadOnlyList<RatingJournalEntry> entries)
        => [.. Ordered(entries)
            .Where(entry => entry.Date is not null)
            .Select(entry => new RatingPoint(entry.Date!.Value, entry.GameId, entry.RatingAfter, entry.MeanAfter, entry.DeviationAfter))];

    /// <summary>The rating line of every board, by the board's technical name.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<RatingPoint>> SeriesPerBoard(IReadOnlyDictionary<string, IReadOnlyList<RatingJournalEntry>> entriesPerBoard)
        => entriesPerBoard.ToDictionary(board => board.Key, board => Series(board.Value));

    private static RatingJournalEntry[] Ordered(IReadOnlyList<RatingJournalEntry> entries)
        => [.. entries.OrderBy(entry => entry.Date ?? DateTimeOffset.MinValue).ThenBy(entry => entry.GameId)];

    private static int Display(double mean, double deviation)
        => FAForever.FileFormats.Replay.ReplayPlayerOptions.DisplayRating(mean, deviation) ?? 0;
}
