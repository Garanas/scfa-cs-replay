using System.Globalization;
using FAForever.FileFormats.Blueprints;
using FAForever.Vault.Viewer.Features.Players.Charts;
using FAForever.Vault.Viewer.Features.Players.Matches;
using FAForever.Vault.Viewer.Services.Players.Statistics;
using FAForever.Vault.Viewer.Services.Theming;

namespace FAForever.Vault.Viewer.Features.Players.Army;

/// <summary>One part of a <c>StackedBar</c>.</summary>
public sealed record StackedSegment(string Label, long Value, ChartColor Color);

/// <summary>A row of units built and lost; the fractions are of the largest value of the panel the row is in.</summary>
public sealed record BuiltLostRow(string Label, long Built, long Lost, double BuiltFraction, double LostFraction, string LostShare);

/// <summary>The wording and rows of the Army tab.</summary>
public static class ArmyFormat
{
    /// <summary>The games over every faction.</summary>
    public static long Games(ArmySummary summary) => summary.Factions.Sum(faction => faction.Plays);

    /// <summary>The share of the games won over every faction (0 to 1); null without games.</summary>
    public static double? WinShare(ArmySummary summary)
        => Games(summary) is > 0 and var games ? (double)summary.Factions.Sum(faction => faction.Wins) / games : null;

    /// <summary>Hours with a thousands separator: 1,342 h.</summary>
    public static string Hours(double hours) => $"{hours.ToString("N0", CultureInfo.InvariantCulture)} h";

    /// <summary>The average game in minutes (the time in game over the games), or null without games.</summary>
    public static string? AverageGame(ArmySummary summary)
        => Games(summary) is > 0 and var games ? $"{(int)Math.Round(summary.HoursPlayed * 60 / games)} min per game" : null;

    /// <summary>Units built or lost in all of the rows.</summary>
    public static long Built(IReadOnlyList<BuiltAndLost> rows) => rows.Sum(row => row.Built);

    /// <summary>Units lost in all of the rows.</summary>
    public static long Lost(IReadOnlyList<BuiltAndLost> rows) => rows.Sum(row => row.Lost);

    /// <summary>The share lost of what was built (0 to 1); null when nothing was built.</summary>
    public static double? LostShare(long built, long lost) => built > 0 ? (double)lost / built : null;

    /// <summary>The ACUs lost per game with two decimals; "No games" without any.</summary>
    public static string PerGame(double? value) => value is { } number ? number.ToString("0.00", CultureInfo.InvariantCulture) : "No games";

    /// <summary>The faction bars: the won games solid, the lost ones pale after them, in the faction's colour.</summary>
    public static IReadOnlyList<BarRow> FactionRows(IReadOnlyList<FactionPlays> factions)
        => [.. factions.Select(faction => new BarRow(
            Factions.Name(faction.Faction) ?? faction.Faction.ToString(),
            faction.Wins,
            faction.Plays == 0 ? "No games" : $"{MatchFormat.Count(faction.Plays)} · {MatchFormat.Percent(faction.WinShare)} won",
            Extra: faction.Losses,
            CssColor: Factions.Swatch(faction.Faction)))];

    /// <summary>The faction played most, or null without games.</summary>
    public static FactionPlays? MainFaction(IReadOnlyList<FactionPlays> factions)
        => factions.Where(faction => faction.Plays > 0).OrderByDescending(faction => faction.Plays).FirstOrDefault();

    /// <summary>The line naming the main faction: Cybran, 46% of games.</summary>
    public static string MainFactionText(FactionPlays main, long games)
        => $"{Factions.Name(main.Faction)}, {MatchFormat.Percent(games > 0 ? (double)main.Plays / games : null)} of games";

    /// <summary>The legend of the faction bars: won and lost, in a neutral colour (the bars carry the faction colours).</summary>
    public static IReadOnlyList<LegendItem> WonLostLegend { get; } =
    [
        new("Won", ChartColor.Faint, Opacity: 1),
        new("Lost", ChartColor.Faint, Opacity: 0.35),
    ];

    /// <summary>The legend of the built and lost bars.</summary>
    public static IReadOnlyList<LegendItem> BuiltLostLegend { get; } =
    [
        new("Built", ChartColor.Viz1),
        new("Lost", ChartColor.Viz2),
    ];

    /// <summary>Custom games and matchmaker games as the two parts of one bar.</summary>
    public static IReadOnlyList<StackedSegment> ModeSegments(ArmySummary summary)
        => [
            new($"Custom games", summary.CustomGames, ChartColor.Viz1),
            new($"Matchmaker", summary.LadderGames, ChartColor.Viz2),
        ];

    /// <summary>
    /// Rows of units built and lost; one scale for the whole panel, so a small row is a small bar
    /// (land dwarfs naval on purpose).
    /// </summary>
    public static IReadOnlyList<BuiltLostRow> BuiltLostRows(IReadOnlyList<BuiltAndLost> rows)
    {
        long max = rows.Count == 0 ? 0 : rows.Max(row => Math.Max(row.Built, row.Lost));
        return [.. rows.Select(row => new BuiltLostRow(
            row.Label,
            row.Built,
            row.Lost,
            max > 0 ? (double)row.Built / max : 0,
            max > 0 ? (double)row.Lost / max : 0,
            row.Built > 0 ? $"{MatchFormat.Percent(LostShare(row.Built, row.Lost))} lost" : "None built"))];
    }
}
