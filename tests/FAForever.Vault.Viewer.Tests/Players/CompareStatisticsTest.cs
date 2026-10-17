using FAForever.FileFormats.Blueprints;
using FAForever.Vault.Viewer.Features.Players.Compare;
using FAForever.Vault.Viewer.Services.Players;
using FAForever.Vault.Viewer.Services.Players.Statistics;

namespace FAForever.Vault.Viewer.Tests.Players;

[TestClass]
public class CompareStatisticsTest
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    private static RatingJournalEntry Game(int id, double before, double after)
        => new(id, Now.AddDays(-id), before, 0, after, 0);

    private static PlayerEventCounts Events(long ladder, long custom, long seconds, long acus)
        => new(new Dictionary<string, long>
        {
            [PlayerEvents.LadderGamesPlayed] = ladder,
            [PlayerEvents.CustomGamesPlayed] = custom,
            [PlayerEvents.UefPlays] = ladder + custom,
            [PlayerEvents.SecondsPlayed] = seconds,
            [PlayerEvents.AcusLost] = acus,
        });

    private static HeadToHeadGame Game(MatchResult a, MatchResult b, int? teamA = 1, int? teamB = 2)
        => new(1, "g", Now, new MatchParticipant(1, "A", teamA, Faction.Uef, null, a), new MatchParticipant(2, "B", teamB, Faction.Aeon, null, b));

    [TestMethod]
    [DataRow(5.0, 3.0, false, Better.A)]
    [DataRow(3.0, 5.0, false, Better.B)]
    [DataRow(5.0, 5.0, false, Better.Neither)]
    [DataRow(0.2, 0.4, true, Better.A)]
    [DataRow(0.4, 0.2, true, Better.B)]
    public void DecideFollowsTheDirection(double a, double b, bool lowerIsBetter, Better expected)
        => Assert.AreEqual(expected, CompareStatistics.Decide(a, b, lowerIsBetter));

    [TestMethod]
    public void DecideIsNeitherWhenAValueIsMissing()
    {
        Assert.AreEqual(Better.Neither, CompareStatistics.Decide(null, 3, false));
        Assert.AreEqual(Better.Neither, CompareStatistics.Decide(3, null, true));
    }

    [TestMethod]
    public void FactsMarkTheBetterSide()
    {
        PlayerFacts a = new([Game(1, 1000, 1010), Game(2, 1010, 1020)], Events(100, 50, 3600 * 200, 30), 12);
        PlayerFacts b = new([Game(1, 1000, 990)], Events(40, 10, 3600 * 90, 5), 20);

        IReadOnlyList<FactRow> rows = CompareStatistics.Facts(a, b, 40, Now);
        FactRow Row(string label) => rows.Single(row => row.Label == label);

        Assert.AreEqual(Better.A, Row("Games in the period").Better);
        Assert.AreEqual("2", Row("Games in the period").A);
        Assert.AreEqual(Better.A, Row("Win share in the period").Better);
        Assert.AreEqual("100%", Row("Win share in the period").A);
        Assert.AreEqual("0%", Row("Win share in the period").B);
        Assert.AreEqual(Better.A, Row("Peak rating in the period").Better);
        Assert.AreEqual(Better.A, Row("Games with a faction recorded").Better);
        Assert.AreEqual("150", Row("Games with a faction recorded").A);
        Assert.IsFalse(rows.Any(row => row.Label == "Hours in game"), "the game no longer records time played, so the row is gone");
        Assert.AreEqual(Better.B, Row("ACUs lost per game").Better);
        Assert.AreEqual("0.20", Row("ACUs lost per game").A);
        Assert.AreEqual(Better.B, Row("Achievements unlocked").Better);
        Assert.AreEqual("12 of 40", Row("Achievements unlocked").A);
    }

    [TestMethod]
    public void FactsWithoutGamesHaveNoBetterSide()
    {
        PlayerFacts empty = new([], Events(0, 0, 0, 0), 0);
        IReadOnlyList<FactRow> rows = CompareStatistics.Facts(empty, empty, 40, Now);

        Assert.IsTrue(rows.All(row => row.Better == Better.Neither));
        Assert.AreEqual("None", rows.Single(row => row.Label == "Win share in the period").A);
    }

    [TestMethod]
    public void AbsentCountersGiveNotRecordedAndDivideByTheFactionGames()
    {
        PlayerEventCounts events = new(new Dictionary<string, long>
        {
            [PlayerEvents.UefPlays] = 60,
            [PlayerEvents.CybranPlays] = 40,
            [PlayerEvents.AcusLost] = 50,
        });
        PlayerFacts a = new([], events, 0);
        IReadOnlyList<FactRow> rows = CompareStatistics.Facts(a, a, 40, Now);
        FactRow Row(string label) => rows.Single(row => row.Label == label);

        Assert.AreEqual("100", Row("Games with a faction recorded").A);
        Assert.AreEqual("0.50", Row("ACUs lost per game").A);
    }

    [TestMethod]
    public void WinnerComesFromTheResults()
    {
        Assert.AreEqual(HeadToHeadWinner.A, CompareStatistics.Winner(Game(MatchResult.Victory, MatchResult.Defeat)));
        Assert.AreEqual(HeadToHeadWinner.B, CompareStatistics.Winner(Game(MatchResult.Defeat, MatchResult.Victory)));
        Assert.AreEqual(HeadToHeadWinner.None, CompareStatistics.Winner(Game(MatchResult.Draw, MatchResult.Draw)));
        Assert.AreEqual(HeadToHeadWinner.None, CompareStatistics.Winner(Game(MatchResult.Unknown, MatchResult.Unknown)));
    }

    [TestMethod]
    public void RecordCountsSameTeamGamesApart()
    {
        HeadToHeadGame[] games =
        [
            Game(MatchResult.Victory, MatchResult.Defeat),
            Game(MatchResult.Victory, MatchResult.Defeat),
            Game(MatchResult.Defeat, MatchResult.Victory),
            Game(MatchResult.Draw, MatchResult.Draw),
            Game(MatchResult.Victory, MatchResult.Victory, 2, 2),
        ];

        HeadToHeadRecord record = CompareStatistics.Record(games);

        Assert.AreEqual(2, record.AWins);
        Assert.AreEqual(1, record.BWins);
        Assert.AreEqual(1, record.Undecided);
        Assert.AreEqual(1, record.SameTeam);
        Assert.AreEqual(4, record.Opposing);
        Assert.AreEqual(4, CompareStatistics.Opposing(games).Count);
    }

    [TestMethod]
    public void FactionSharesAddUpToOne()
    {
        PlayerEventCounts events = new(new Dictionary<string, long>
        {
            [PlayerEvents.UefPlays] = 10,
            [PlayerEvents.CybranPlays] = 30,
        });

        IReadOnlyList<FactionShare> shares = CompareStatistics.FactionShares(events);

        Assert.AreEqual(4, shares.Count);
        Assert.AreEqual(0.25, shares.Single(s => s.Faction == Faction.Uef).Share, 1e-9);
        Assert.AreEqual(0.75, shares.Single(s => s.Faction == Faction.Cybran).Share, 1e-9);
        Assert.AreEqual(0, shares.Single(s => s.Faction == Faction.Seraphim).Share);
        Assert.AreEqual(0, CompareStatistics.FactionShares(new PlayerEventCounts(new Dictionary<string, long>())).Sum(s => s.Share));
    }

    [TestMethod]
    public void RatingRowsKeepBoardsOfBothAndTheGapFromAsView()
    {
        PlayerRating Rating(string board, double mean) => new(new Leaderboard(1, board), mean, 0, 10, 5);
        PlayerRating[] a = [Rating("tmm_2v2", 1500), Rating("ladder_1v1", 1700), Rating("global", 1200)];
        PlayerRating[] b = [Rating("ladder_1v1", 1800), Rating("tmm_2v2", 1400)];

        var rows = CompareStatistics.RatingRows(a, b);

        Assert.AreEqual(2, rows.Count);
        Assert.AreEqual("Ladder 1v1", rows[0].Label);
        Assert.AreEqual("100 behind", rows[0].GapText);
        Assert.AreEqual(Features.Players.Charts.DeltaTone.Bad, rows[0].GapTone);
        Assert.AreEqual("100 ahead", rows[1].GapText);
        Assert.AreEqual(Features.Players.Charts.DeltaTone.Good, rows[1].GapTone);
        CollectionAssert.AreEqual(new[] { "Global" }, CompareStatistics.BoardsOfOne(a, b).ToArray());
    }
}
