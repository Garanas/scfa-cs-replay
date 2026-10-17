using System.Globalization;
using FAForever.Vault.Viewer.Features.Maps;
using FAForever.Vault.Viewer.Features.Players.Charts;
using FAForever.Vault.Viewer.Services.Players;
using FAForever.Vault.Viewer.Services.Players.Statistics;
using FAForever.Vault.Viewer.Services.Theming;

namespace FAForever.Vault.Viewer.Features.Players.Matches;

/// <summary>How the Matches tab words its numbers, and the rows its charts are drawn from.</summary>
public static class MatchFormat
{
    /// <summary>What a game without a vault map is called (generated maps and maps without a vault entry).</summary>
    public const string MapNotInVault = "Map not in the vault";

    /// <summary>The minus sign (U+2212): a sign, not a hyphen.</summary>
    public const string Minus = "−";

    private static readonly string[] Days = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];

    /// <summary>The weekday labels of the heatmap, Monday first.</summary>
    public static IReadOnlyList<string> DayLabels { get; } = [.. Days.Select(day => day[..3])];

    /// <summary>The start hour of each block of the heatmap: 0, 4 and so on up to 20.</summary>
    public static IReadOnlyList<string> BlockLabels { get; } =
        [.. Enumerable.Range(0, 24 / MatchStatistics.HoursPerBlock).Select(block => (block * MatchStatistics.HoursPerBlock).ToString(CultureInfo.InvariantCulture))];

    /// <summary>A count with thousands separators.</summary>
    public static string Count(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>A share (0 to 1) as a whole percentage; "No result" when there is none.</summary>
    public static string Percent(double? share) => share is { } value ? $"{(int)Math.Round(value * 100, MidpointRounding.AwayFromZero)}%" : "No result";

    /// <summary>A whole number with its sign: +14, then the minus sign and 3, or 0.</summary>
    public static string Signed(int value) => value switch
    {
        > 0 => "+" + value.ToString(CultureInfo.InvariantCulture),
        < 0 => Minus + (-value).ToString(CultureInfo.InvariantCulture),
        _ => "0",
    };

    /// <summary>A number with one decimal and its sign.</summary>
    public static string SignedDecimal(double value)
    {
        double rounded = Math.Round(value, 1, MidpointRounding.AwayFromZero);
        string text = Math.Abs(rounded).ToString("0.0", CultureInfo.InvariantCulture);
        return rounded > 0 ? "+" + text : rounded < 0 ? Minus + text : text;
    }

    /// <summary>The tone of a rating change: a rise is good, a fall bad.</summary>
    public static DeltaTone Tone(double change) => change > 0 ? DeltaTone.Good : change < 0 ? DeltaTone.Bad : DeltaTone.Neutral;

    /// <summary>The length of a game as 42:10 or 1:02:10; "Unknown" without one.</summary>
    public static string Length(TimeSpan? length)
    {
        if (length is not { } span)
        {
            return "Unknown";
        }

        int seconds = (int)Math.Round(span.TotalSeconds);
        int hours = seconds / 3600;
        int minutes = seconds % 3600 / 60;
        return hours > 0
            ? $"{hours}:{minutes:00}:{seconds % 60:00}"
            : $"{minutes}:{seconds % 60:00}";
    }

    /// <summary>The mean length in whole minutes: 32 min.</summary>
    public static string Minutes(TimeSpan? length)
        => length is { } span ? $"{(int)Math.Round(span.TotalMinutes, MidpointRounding.AwayFromZero)} min" : "Unknown";

    /// <summary>The word of a result.</summary>
    public static string ResultWord(MatchResult result) => result switch
    {
        MatchResult.Victory => "Win",
        MatchResult.Defeat => "Loss",
        MatchResult.Draw => "Draw",
        _ => "Unknown",
    };

    /// <summary>The mode of a game: its board's name; a game no matchmaker board rated is custom.</summary>
    public static string Mode(string? board)
        => string.IsNullOrEmpty(board) || board == "global" ? "Custom" : Leaderboards.DisplayName(board);

    /// <summary>The moment of a game in a time zone (the viewer's by default) as 14 Oct 2026, 13:09.</summary>
    public static string When(DateTimeOffset? start, TimeZoneInfo? zone = null)
        => start is { } moment
            ? TimeZoneInfo.ConvertTime(moment, zone ?? TimeZoneInfo.Local).ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture)
            : "Unknown";

    /// <summary>The count and a noun: 1 game, 12 games.</summary>
    public static string Games(long count) => $"{Count(count)} {(count == 1 ? "game" : "games")}";

    /// <summary>The record of a summary: 70 wins, 50 losses, 3 draws (draws only when there are some).</summary>
    public static string Record(MatchSummary summary)
    {
        string text = $"{Count(summary.Wins)} {(summary.Wins == 1 ? "win" : "wins")}, {Count(summary.Losses)} {(summary.Losses == 1 ? "loss" : "losses")}";
        return summary.Draws > 0 ? $"{text}, {Count(summary.Draws)} {(summary.Draws == 1 ? "draw" : "draws")}" : text;
    }

    /// <summary>The label of a length column: Under 10, 10 to 20, 45+.</summary>
    public static string LengthLabel(LengthBucket bucket) => bucket switch
    {
        { FromMinutes: 0, ToMinutes: { } to } => $"Under {to}",
        { ToMinutes: { } to } => $"{bucket.FromMinutes} to {to}",
        _ => $"{bucket.FromMinutes}+",
    };

    /// <summary>The win share of each faction as bars (0 to 100), in the faction's colour, the games as the second line.</summary>
    public static IReadOnlyList<BarRow> FactionRows(IReadOnlyList<FactionRecord> records)
        => [.. records.Select(record => new BarRow(
            Factions.Name(record.Faction) ?? record.Faction.ToString(),
            (record.WinShare ?? 0) * 100,
            record.Games == 0 ? "No games" : Percent(record.WinShare),
            Sub: Games(record.Games),
            CssColor: Factions.Swatch(record.Faction)))];

    /// <summary>The win share of each length as columns (0 to 100); the games sit in the category label under the column.</summary>
    public static IReadOnlyList<ColumnItem> LengthColumns(IReadOnlyList<LengthBucket> buckets)
        => [.. buckets.Select(bucket => new ColumnItem(
            $"{LengthLabel(bucket)}\n{Games(bucket.Games)}",
            (bucket.WinShare ?? 0) * 100,
            bucket.WinShare is null ? null : Percent(bucket.WinShare),
            Title: bucket.WinShare is null
                ? $"{LengthLabel(bucket)} min: no games with a result"
                : $"{LengthLabel(bucket)} min: {Count(bucket.Wins)} of {Count(bucket.Wins + bucket.Losses)} won"))];

    /// <summary>The most played maps as bars of the win share; a map whose folder the games name links to its page.</summary>
    public static IReadOnlyList<BarRow> MapRows(IReadOnlyList<PlayerMatch> matches, int top = 8)
    {
        Dictionary<string, string> folders = [];
        foreach (PlayerMatch match in matches)
        {
            if (match.MapName is { Length: > 0 } name && match.MapFolder is { Length: > 0 } folder)
            {
                folders.TryAdd(name, folder);
            }
        }

        return [.. MatchStatistics.ByMap(matches, top).Select(record => new BarRow(
            record.Map,
            (record.WinShare ?? 0) * 100,
            Percent(record.WinShare),
            Sub: Games(record.Games),
            Href: folders.TryGetValue(record.Map, out string? folder) ? MapLinks.Map(folder) : null))];
    }

    /// <summary>The words for one block of the heatmap: 16 to 20.</summary>
    public static string BlockRange(int block)
        => $"{block * MatchStatistics.HoursPerBlock} to {(block + 1) * MatchStatistics.HoursPerBlock}";

    /// <summary>The words for one cell: Saturday, 16 to 20: 12 games.</summary>
    public static string CellText(int day, int block, double games)
        => $"{Days[day]}, {BlockRange(block)}: {Games((long)games)}";

    /// <summary>The heatmap cells as numbers for the <c>Heatmap</c> component.</summary>
    public static IReadOnlyList<IReadOnlyList<double>> HeatValues(IReadOnlyList<IReadOnlyList<int>> cells)
        => [.. cells.Select(row => (IReadOnlyList<double>)[.. row.Select(value => (double)value)])];

    /// <summary>The busiest cell in words (Saturday, 16 to 20: 12 games), or null without games.</summary>
    public static string? Busiest(IReadOnlyList<IReadOnlyList<int>> cells)
    {
        int best = 0;
        int bestDay = 0;
        int bestBlock = 0;
        for (int day = 0; day < cells.Count; day++)
        {
            for (int block = 0; block < cells[day].Count; block++)
            {
                if (cells[day][block] > best)
                {
                    best = cells[day][block];
                    bestDay = day;
                    bestBlock = block;
                }
            }
        }

        return best == 0 ? null : CellText(bestDay, bestBlock, best);
    }

    /// <summary>
    /// The other players of a game split into the player's team mates and the opponents. Team 1 (and
    /// no team) means everybody plays alone, so everybody else is an opponent.
    /// </summary>
    public static (IReadOnlyList<MatchParticipant> Mates, IReadOnlyList<MatchParticipant> Opponents) Others(PlayerMatch match, int playerId)
    {
        MatchParticipant[] others = [.. match.Players.Where(player => player.PlayerId != playerId)];
        bool inTeam = match.Team is > 1;
        return (
            [.. others.Where(player => inTeam && player.Team == match.Team)],
            [.. others.Where(player => !(inTeam && player.Team == match.Team))]);
    }
}
