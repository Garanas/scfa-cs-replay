using FAForever.FileFormats.Blueprints;

namespace FAForever.Vault.Viewer.Services.Players.Statistics;

/// <summary>The tiles above the matches.</summary>
/// <param name="Games">All games.</param>
/// <param name="Wins">Victories.</param>
/// <param name="Losses">Defeats.</param>
/// <param name="Draws">Draws.</param>
/// <param name="WinShare">Wins as a share of wins and losses (0 to 1); null without either.</param>
/// <param name="AverageLength">The mean in-game duration of the games that have one.</param>
/// <param name="AverageRatingChange">The mean rating change of the rated games.</param>
/// <param name="MostPlayedMap">The map with most games.</param>
/// <param name="MostPlayedMapGames">The number of games on it.</param>
public sealed record MatchSummary(
    int Games,
    int Wins,
    int Losses,
    int Draws,
    double? WinShare,
    TimeSpan? AverageLength,
    double? AverageRatingChange,
    string? MostPlayedMap,
    int MostPlayedMapGames);

/// <summary>Wins and losses of one faction; <c>WinShare</c> is wins of wins and losses.</summary>
public sealed record FactionRecord(Faction Faction, int Games, int Wins, int Losses)
{
    /// <summary>Wins as a share of wins and losses (0 to 1); null without either.</summary>
    public double? WinShare => MatchStatistics.Share(Wins, Losses);
}

/// <summary>Wins and losses on one map.</summary>
public sealed record MapRecord(string Map, int Games, int Wins, int Losses)
{
    /// <summary>Wins as a share of wins and losses (0 to 1); null without either.</summary>
    public double? WinShare => MatchStatistics.Share(Wins, Losses);
}

/// <summary>Wins and losses of games of one length, <c>FromMinutes</c> inclusive and <c>ToMinutes</c> exclusive (null for no limit).</summary>
public sealed record LengthBucket(string Label, int FromMinutes, int? ToMinutes, int Games, int Wins, int Losses)
{
    /// <summary>Wins as a share of wins and losses (0 to 1); null without either.</summary>
    public double? WinShare => MatchStatistics.Share(Wins, Losses);
}

/// <summary>Statistics over a list of a player's games.</summary>
public static class MatchStatistics
{
    /// <summary>The block of hours in the heat map: 6 blocks of 4 hours a day.</summary>
    public const int HoursPerBlock = 4;

    private static readonly (string Label, int From, int? To)[] LengthBuckets =
    [
        ("Under 10 min", 0, 10),
        ("10 to 20 min", 10, 20),
        ("20 to 30 min", 20, 30),
        ("30 to 45 min", 30, 45),
        ("45 min and up", 45, null),
    ];

    /// <summary>Wins as a share of wins and losses; null when there are neither.</summary>
    public static double? Share(int wins, int losses) => wins + losses > 0 ? (double)wins / (wins + losses) : null;

    /// <summary>The tiles over the games.</summary>
    public static MatchSummary Summarize(IReadOnlyList<PlayerMatch> matches)
    {
        int wins = matches.Count(match => match.Result == MatchResult.Victory);
        int losses = matches.Count(match => match.Result == MatchResult.Defeat);
        int draws = matches.Count(match => match.Result == MatchResult.Draw);

        TimeSpan[] lengths = [.. matches.Where(match => match.Duration is not null).Select(match => match.Duration!.Value)];
        double[] changes = [.. matches.Select(match => match.RatingChange).OfType<int>().Select(change => (double)change)];
        MapRecord? top = ByMap(matches, 1).FirstOrDefault();

        return new MatchSummary(
            matches.Count,
            wins,
            losses,
            draws,
            Share(wins, losses),
            lengths.Length > 0 ? TimeSpan.FromTicks((long)lengths.Average(length => (double)length.Ticks)) : null,
            changes.Length > 0 ? changes.Average() : null,
            top?.Map,
            top?.Games ?? 0);
    }

    /// <summary>Games, wins and losses of each of the four factions, in the game's order (UEF, Aeon, Cybran, Seraphim).</summary>
    public static IReadOnlyList<FactionRecord> ByFaction(IReadOnlyList<PlayerMatch> matches)
        => [.. new[] { Faction.Uef, Faction.Aeon, Faction.Cybran, Faction.Seraphim }.Select(faction =>
        {
            PlayerMatch[] played = [.. matches.Where(match => match.Faction == faction)];
            return new FactionRecord(
                faction,
                played.Length,
                played.Count(match => match.Result == MatchResult.Victory),
                played.Count(match => match.Result == MatchResult.Defeat));
        })];

    /// <summary>The maps with most games first, at most <paramref name="top"/> of them; games without a map are left out.</summary>
    public static IReadOnlyList<MapRecord> ByMap(IReadOnlyList<PlayerMatch> matches, int top = 8)
        => [.. matches
            .Where(match => !string.IsNullOrEmpty(match.MapName))
            .GroupBy(match => match.MapName!)
            .Select(group => new MapRecord(
                group.Key,
                group.Count(),
                group.Count(match => match.Result == MatchResult.Victory),
                group.Count(match => match.Result == MatchResult.Defeat)))
            .OrderByDescending(record => record.Games)
            .ThenBy(record => record.Map, StringComparer.Ordinal)
            .Take(top)];

    /// <summary>
    /// The games by length: under 10, 10 to 20, 20 to 30, 30 to 45 and 45 minutes and up. Games
    /// without a duration are left out.
    /// </summary>
    public static IReadOnlyList<LengthBucket> ByLength(IReadOnlyList<PlayerMatch> matches)
        => [.. LengthBuckets.Select(bucket =>
        {
            PlayerMatch[] inBucket = [.. matches.Where(match => match.Duration is { } length
                && length.TotalMinutes >= bucket.From
                && (bucket.To is not { } to || length.TotalMinutes < to))];
            return new LengthBucket(
                bucket.Label,
                bucket.From,
                bucket.To,
                inBucket.Length,
                inBucket.Count(match => match.Result == MatchResult.Victory),
                inBucket.Count(match => match.Result == MatchResult.Defeat));
        })];

    /// <summary>
    /// Games per weekday (index 0 is Monday) and block of 4 hours (index 0 is midnight to 4 a.m.), by
    /// the start time in the given time zone (the viewer's local one by default).
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<int>> Heatmap(IReadOnlyList<PlayerMatch> matches, TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Local;
        int blocks = 24 / HoursPerBlock;
        int[][] cells = [.. Enumerable.Range(0, 7).Select(_ => new int[blocks])];
        foreach (PlayerMatch match in matches)
        {
            if (match.StartTime is not { } start)
            {
                continue;
            }

            DateTime local = TimeZoneInfo.ConvertTime(start, zone).DateTime;
            int weekday = ((int)local.DayOfWeek + 6) % 7;
            cells[weekday][local.Hour / HoursPerBlock]++;
        }
        return cells;
    }
}
