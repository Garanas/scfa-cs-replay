using System.Globalization;
using FAForever.FileFormats.Blueprints;
using FAForever.Vault.Viewer.Features.Players.Charts;
using FAForever.Vault.Viewer.Features.Players.Army;
using FAForever.Vault.Viewer.Services.Players;
using FAForever.Vault.Viewer.Services.Players.Statistics;

namespace FAForever.Vault.Viewer.Features.Players.Compare;

/// <summary>Which of two players has the better value of a fact.</summary>
public enum Better
{
    /// <summary>A tie, or a value that one side lacks.</summary>
    Neither,

    /// <summary>The first player.</summary>
    A,

    /// <summary>The second player.</summary>
    B,
}

/// <summary>One row of the "Side by side" table.</summary>
public sealed record FactRow(string Label, string A, string B, Better Better);

/// <summary>What the facts table needs of one player.</summary>
public sealed record PlayerFacts(
    IReadOnlyList<RatingJournalEntry> Journal,
    PlayerEventCounts Events,
    int AchievementsUnlocked);

/// <summary>Who won a game two players were in together.</summary>
public enum HeadToHeadWinner
{
    /// <summary>No winner: a draw, or no result reported.</summary>
    None,

    /// <summary>The first player won.</summary>
    A,

    /// <summary>The second player won.</summary>
    B,
}

/// <summary>The record of two players against each other; games on one team are counted apart.</summary>
public sealed record HeadToHeadRecord(int AWins, int BWins, int Undecided, int SameTeam)
{
    /// <summary>The games the two played against each other.</summary>
    public int Opposing => AWins + BWins + Undecided;
}

/// <summary>A faction's share of a player's games.</summary>
public sealed record FactionShare(Faction Faction, long Plays, double Share);

/// <summary>The pure parts of the comparison page.</summary>
public static class CompareStatistics
{
    /// <summary>Builds the rows of the "Side by side" table.</summary>
    public static IReadOnlyList<FactRow> Facts(PlayerFacts a, PlayerFacts b, int achievementsTotal, DateTimeOffset now)
    {
        RatingSummary summaryA = RatingStatistics.Summarize(a.Journal, now);
        RatingSummary summaryB = RatingStatistics.Summarize(b.Journal, now);
        ArmySummary armyA = ArmyStatistics.Summarize(a.Events);
        ArmySummary armyB = ArmyStatistics.Summarize(b.Events);

        double? winA = summaryA.Games > 0 ? (double)summaryA.Wins / summaryA.Games : null;
        double? winB = summaryB.Games > 0 ? (double)summaryB.Wins / summaryB.Games : null;

        return
        [
            Row("Games in the period", summaryA.Games, summaryB.Games, Count),
            Row("Win share in the period", winA, winB, Percent),
            Row("Peak rating in the period", summaryA.Peak, summaryB.Peak, Count),
            Row("Games with a faction recorded", ArmyFormat.Games(armyA), ArmyFormat.Games(armyB), Count),
            HoursRow(armyA.HoursPlayed, armyB.HoursPlayed),
            Row("ACUs lost per game", armyA.AcusLostPerGame, armyB.AcusLostPerGame, value => value.ToString("0.00", CultureInfo.InvariantCulture), lowerIsBetter: true),
            new FactRow(
                "Achievements unlocked",
                $"{a.AchievementsUnlocked:N0} of {achievementsTotal:N0}",
                $"{b.AchievementsUnlocked:N0} of {achievementsTotal:N0}",
                Decide(a.AchievementsUnlocked, b.AchievementsUnlocked, false)),
        ];
    }

    /// <summary>Which side is better: the higher value, or the lower one when <paramref name="lowerIsBetter"/>.</summary>
    public static Better Decide(double? a, double? b, bool lowerIsBetter)
    {
        if (a is not { } first || b is not { } second || first.Equals(second))
        {
            return Better.Neither;
        }

        bool aWins = lowerIsBetter ? first < second : first > second;
        return aWins ? Better.A : Better.B;
    }

    /// <summary>The winner of a game, from the two players' results.</summary>
    public static HeadToHeadWinner Winner(HeadToHeadGame game)
    {
        bool a = game.A.Result == MatchResult.Victory;
        bool b = game.B.Result == MatchResult.Victory;
        return a == b ? HeadToHeadWinner.None : a ? HeadToHeadWinner.A : HeadToHeadWinner.B;
    }

    /// <summary>The record: wins of each, undecided games, and the games on one team.</summary>
    public static HeadToHeadRecord Record(IReadOnlyList<HeadToHeadGame> games)
    {
        int aWins = 0, bWins = 0, undecided = 0, same = 0;
        foreach (HeadToHeadGame game in games)
        {
            if (game.SameTeam)
            {
                same++;
                continue;
            }

            switch (Winner(game))
            {
                case HeadToHeadWinner.A: aWins++; break;
                case HeadToHeadWinner.B: bWins++; break;
                default: undecided++; break;
            }
        }

        return new HeadToHeadRecord(aWins, bWins, undecided, same);
    }

    /// <summary>The opposing games, newest first.</summary>
    public static IReadOnlyList<HeadToHeadGame> Opposing(IReadOnlyList<HeadToHeadGame> games)
        => [.. games.Where(game => !game.SameTeam).OrderByDescending(game => game.StartTime ?? DateTimeOffset.MinValue)];

    /// <summary>The share of each faction among the games the four faction counters know (zero shares for a player with none).</summary>
    public static IReadOnlyList<FactionShare> FactionShares(PlayerEventCounts events)
    {
        IReadOnlyList<FactionPlays> plays = ArmyStatistics.Summarize(events).Factions;
        long total = plays.Sum(p => p.Plays);
        return [.. plays.Select(p => new FactionShare(p.Faction, p.Plays, total > 0 ? (double)p.Plays / total : 0))];
    }

    /// <summary>The rows of the dumbbell: the boards both players have, the gap from A's view.</summary>
    public static IReadOnlyList<DumbbellRow> RatingRows(IReadOnlyList<PlayerRating> a, IReadOnlyList<PlayerRating> b)
    {
        var rows = new List<DumbbellRow>();
        foreach (PlayerRating first in a.OrderBy(r => BoardOrder(r.Board.TechnicalName)))
        {
            PlayerRating? second = b.FirstOrDefault(r => r.Board.TechnicalName == first.Board.TechnicalName);
            if (second is null)
            {
                continue;
            }

            int gap = first.Rating - second.Rating;
            string text = gap == 0 ? "Level" : gap > 0 ? $"{gap:N0} ahead" : $"{-gap:N0} behind";
            rows.Add(new DumbbellRow(
                first.Board.DisplayName,
                first.Rating,
                second.Rating,
                first.Rating.ToString("N0", CultureInfo.InvariantCulture),
                second.Rating.ToString("N0", CultureInfo.InvariantCulture),
                text)
            {
                GapTone = gap == 0 ? DeltaTone.Neutral : gap > 0 ? DeltaTone.Good : DeltaTone.Bad,
            });
        }

        return rows;
    }

    /// <summary>Boards one player has and the other lacks, by display name.</summary>
    public static IReadOnlyList<string> BoardsOfOne(IReadOnlyList<PlayerRating> a, IReadOnlyList<PlayerRating> b)
        => [.. a.Select(r => r.Board.TechnicalName).Except(b.Select(r => r.Board.TechnicalName))
            .Concat(b.Select(r => r.Board.TechnicalName).Except(a.Select(r => r.Board.TechnicalName)))
            .Select(Leaderboards.DisplayName)];

    private static int BoardOrder(string technicalName)
    {
        int index = PlayerFilters.Boards.ToList().FindIndex(option => option.Token == technicalName);
        return index < 0 ? int.MaxValue : index;
    }

    private static string Count(double value) => Math.Round(value).ToString("N0", CultureInfo.InvariantCulture);

    private static string Percent(double value) => (value * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

    private static FactRow HoursRow(double? a, double? b)
        => a is null && b is null
            ? new FactRow("Hours in game", "Not recorded", "Not recorded", Better.Neither)
            : Row("Hours in game", a, b, Count);

    private static FactRow Row(string label, double? a, double? b, Func<double, string> format, bool lowerIsBetter = false)
        => new(label, a is { } x ? format(x) : "None", b is { } y ? format(y) : "None", Decide(a, b, lowerIsBetter));
}
