using System.Globalization;
using FAForever.Vault.Viewer.Features.Players.Charts;
using FAForever.Vault.Viewer.Services.Players;
using FAForever.Vault.Viewer.Services.Players.Statistics;

namespace FAForever.Vault.Viewer.Features.Players.Profile;

/// <summary>
/// The words and numbers of the profile pages as text. Signs use the minus sign (U+2212), never a
/// hyphen; dates are in the viewer's time zone unless a zone is given (for tests).
/// </summary>
public static class ProfileFormat
{
    /// <summary>The minus sign.</summary>
    public const string Minus = "−";

    /// <summary>A whole number with thousands separators: <c>1,316</c>.</summary>
    public static string Number(double value)
        => Math.Round(value, MidpointRounding.AwayFromZero).ToString("N0", CultureInfo.InvariantCulture).Replace("-", Minus, StringComparison.Ordinal);

    /// <summary>A number with its sign: <c>+12</c>, <c>−5</c>, or <c>0</c> when it rounds to nothing.</summary>
    public static string Signed(double value)
    {
        double rounded = Math.Round(value, MidpointRounding.AwayFromZero);
        return rounded > 0
            ? "+" + rounded.ToString("N0", CultureInfo.InvariantCulture)
            : rounded < 0 ? Minus + Math.Abs(rounded).ToString("N0", CultureInfo.InvariantCulture) : "0";
    }

    /// <summary>A number with one decimal and its sign (the average change per game): <c>+1.4</c>, <c>−0.8</c>.</summary>
    public static string SignedOneDecimal(double value)
    {
        double rounded = Math.Round(value, 1, MidpointRounding.AwayFromZero);
        return rounded > 0
            ? "+" + rounded.ToString("0.0", CultureInfo.InvariantCulture)
            : rounded < 0 ? Minus + Math.Abs(rounded).ToString("0.0", CultureInfo.InvariantCulture) : "0.0";
    }

    /// <summary>Good for a rise, bad for a fall, neutral for no change (after rounding).</summary>
    public static DeltaTone Tone(double change)
    {
        double rounded = Math.Round(change, MidpointRounding.AwayFromZero);
        return rounded > 0 ? DeltaTone.Good : rounded < 0 ? DeltaTone.Bad : DeltaTone.Neutral;
    }

    /// <summary>"Win" for a rise of the rating, "Loss" for a fall, "No change" otherwise.</summary>
    public static string ResultWord(double ratingChange) => Tone(ratingChange) switch
    {
        DeltaTone.Good => "Win",
        DeltaTone.Bad => "Loss",
        _ => "No change",
    };

    /// <summary>The result of a game as one word.</summary>
    public static string ResultWord(MatchResult result) => result switch
    {
        MatchResult.Victory => "Win",
        MatchResult.Defeat => "Loss",
        MatchResult.Draw => "Draw",
        _ => "No result",
    };

    /// <summary>The tone of a result: a win is good, a defeat is bad.</summary>
    public static DeltaTone Tone(MatchResult result) => result switch
    {
        MatchResult.Victory => DeltaTone.Good,
        MatchResult.Defeat => DeltaTone.Bad,
        _ => DeltaTone.Neutral,
    };

    /// <summary>The mode a game was played in: the board's name, "Custom" for a game no matchmaker board rated.</summary>
    public static string ModeName(string? board)
        => board is null or "global" ? "Custom" : Leaderboards.DisplayName(board);

    /// <summary>A share as a percentage: <c>55%</c>; empty for none.</summary>
    public static string Percent(double? share)
        => share is { } value ? Math.Round(value * 100, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + "%" : "";

    /// <summary>A game's length: <c>42 min</c>, <c>1 h 12 min</c>; empty when unknown.</summary>
    public static string Duration(TimeSpan? length)
    {
        if (length is not { } span)
        {
            return "";
        }

        int minutes = (int)Math.Round(span.TotalMinutes, MidpointRounding.AwayFromZero);
        return minutes >= 60
            ? $"{minutes / 60} h {minutes % 60:00} min"
            : $"{minutes} min";
    }

    /// <summary>A date such as <c>16 Oct 2026</c>; empty when unknown.</summary>
    public static string Date(DateTimeOffset? date, TimeZoneInfo? zone = null)
        => date is { } value ? Convert(value, zone).ToString("d MMM yyyy", CultureInfo.InvariantCulture) : "";

    /// <summary>A month such as <c>Mar 2021</c>; empty when unknown.</summary>
    public static string Month(DateTimeOffset? date, TimeZoneInfo? zone = null)
        => date is { } value ? Convert(value, zone).ToString("MMM yyyy", CultureInfo.InvariantCulture) : "";

    /// <summary>The opponents of a game, the first <paramref name="max"/> by name and the rest counted.</summary>
    public static string Opponents(IReadOnlyList<PlayerReference> opponents, int max = 2)
    {
        if (opponents.Count == 0)
        {
            return "";
        }

        string names = string.Join(", ", opponents.Take(max).Select(opponent => opponent.Login));
        return opponents.Count > max ? $"{names} and {opponents.Count - max} more" : names;
    }

    /// <summary>The words of "Every leaderboard, same year" for a period.</summary>
    public static string SamePeriod(string? period) => Periods.Normalize(period) switch
    {
        Periods.Week => "same week",
        Periods.Month => "same month",
        Periods.Quarter => "same 3 months",
        Periods.All => "all time",
        _ => "same year",
    };

    /// <summary>The words that follow a change over a period: <c>in a year</c>.</summary>
    public static string InPeriod(string? period) => Periods.Normalize(period) switch
    {
        Periods.Week => "in a week",
        Periods.Month => "in a month",
        Periods.Quarter => "in 3 months",
        Periods.All => "over all time",
        _ => "in a year",
    };

    /// <summary>A range of dates as text: <c>16 Oct 2025 to 16 Oct 2026</c>; empty when an end is unknown.</summary>
    public static string Range(DateTimeOffset? from, DateTimeOffset? to, TimeZoneInfo? zone = null)
        => from is null || to is null ? "" : $"{Date(from, zone)} to {Date(to, zone)}";

    private static DateTimeOffset Convert(DateTimeOffset value, TimeZoneInfo? zone)
        => zone is null ? value.ToLocalTime() : TimeZoneInfo.ConvertTime(value, zone);
}
