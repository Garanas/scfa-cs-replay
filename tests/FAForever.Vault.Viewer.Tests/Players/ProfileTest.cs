using FAForever.Vault.Viewer.Features.Players.Charts;
using FAForever.Vault.Viewer.Features.Players.Profile;
using FAForever.Vault.Viewer.Services.Players;

namespace FAForever.Vault.Viewer.Tests.Players;

[TestClass]
public class ProfileFormatTest
{
    private const string Minus = "−";

    [TestMethod]
    [DataRow(12.4, "+12")]
    [DataRow(12.5, "+13")]
    [DataRow(-5.0, Minus + "5")]
    [DataRow(-0.4, "0")]
    [DataRow(0.0, "0")]
    [DataRow(1234.0, "+1,234")]
    public void SignedWritesTheSignAndRounds(double value, string expected)
        => Assert.AreEqual(expected, ProfileFormat.Signed(value));

    [TestMethod]
    [DataRow(1.44, "+1.4")]
    [DataRow(-0.83, Minus + "0.8")]
    [DataRow(0.01, "0.0")]
    public void SignedOneDecimalKeepsOneDecimal(double value, string expected)
        => Assert.AreEqual(expected, ProfileFormat.SignedOneDecimal(value));

    [TestMethod]
    public void NumberHasThousandsSeparatorsAndTheMinusSign()
    {
        Assert.AreEqual("1,316", ProfileFormat.Number(1315.6));
        Assert.AreEqual(Minus + "12", ProfileFormat.Number(-12));
    }

    [TestMethod]
    public void ToneAndResultWordFollowTheSign()
    {
        Assert.AreEqual(DeltaTone.Good, ProfileFormat.Tone(3.2));
        Assert.AreEqual(DeltaTone.Bad, ProfileFormat.Tone(-3.2));
        Assert.AreEqual(DeltaTone.Neutral, ProfileFormat.Tone(0.2));
        Assert.AreEqual("Win", ProfileFormat.ResultWord(3.2));
        Assert.AreEqual("Loss", ProfileFormat.ResultWord(-3.2));
        Assert.AreEqual("No change", ProfileFormat.ResultWord(0.0));
        Assert.AreEqual("Win", ProfileFormat.ResultWord(MatchResult.Victory));
        Assert.AreEqual("Loss", ProfileFormat.ResultWord(MatchResult.Defeat));
        Assert.AreEqual("Draw", ProfileFormat.ResultWord(MatchResult.Draw));
        Assert.AreEqual("No result", ProfileFormat.ResultWord(MatchResult.Unknown));
    }

    [TestMethod]
    public void ModeNameIsCustomWithoutAMatchmakerBoard()
    {
        Assert.AreEqual("Custom", ProfileFormat.ModeName(null));
        Assert.AreEqual("Custom", ProfileFormat.ModeName("global"));
        Assert.AreEqual("Ladder 1v1", ProfileFormat.ModeName("ladder_1v1"));
        Assert.AreEqual("2v2", ProfileFormat.ModeName("tmm_2v2"));
    }

    [TestMethod]
    public void DurationPercentAndOpponents()
    {
        Assert.AreEqual("42 min", ProfileFormat.Duration(TimeSpan.FromMinutes(42)));
        Assert.AreEqual("1 h 05 min", ProfileFormat.Duration(TimeSpan.FromMinutes(65)));
        Assert.AreEqual("", ProfileFormat.Duration(null));
        Assert.AreEqual("55%", ProfileFormat.Percent(0.553));
        Assert.AreEqual("", ProfileFormat.Percent(null));
        Assert.AreEqual("", ProfileFormat.Opponents([]));
        Assert.AreEqual("Kestrel", ProfileFormat.Opponents([new PlayerReference(1, "Kestrel")]));
        Assert.AreEqual("A, B and 1 more", ProfileFormat.Opponents([new PlayerReference(1, "A"), new PlayerReference(2, "B"), new PlayerReference(3, "C")]));
    }

    [TestMethod]
    public void DatesAreInTheGivenZone()
    {
        DateTimeOffset moment = new(2026, 3, 5, 23, 30, 0, TimeSpan.Zero);
        Assert.AreEqual("5 Mar 2026", ProfileFormat.Date(moment, TimeZoneInfo.Utc));
        Assert.AreEqual("Mar 2026", ProfileFormat.Month(moment, TimeZoneInfo.Utc));
        Assert.AreEqual("", ProfileFormat.Date(null));
        Assert.AreEqual("5 Mar 2026 to 6 Mar 2026", ProfileFormat.Range(moment, moment.AddDays(1), TimeZoneInfo.Utc));
        Assert.AreEqual("", ProfileFormat.Range(moment, null));
    }

    [TestMethod]
    public void PeriodWords()
    {
        Assert.AreEqual("same year", ProfileFormat.SamePeriod("1y"));
        Assert.AreEqual("same year", ProfileFormat.SamePeriod(null));
        Assert.AreEqual("same week", ProfileFormat.SamePeriod("1w"));
        Assert.AreEqual("all time", ProfileFormat.SamePeriod("all"));
        Assert.AreEqual("in a year", ProfileFormat.InPeriod("1y"));
        Assert.AreEqual("in 3 months", ProfileFormat.InPeriod("3m"));
    }
}

[TestClass]
public class ProfileSummariesTest
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    private static PlayerRating Rating(string board, int games, double mean = 1500)
        => new(new Leaderboard(1, board), mean, 0, games, games / 2);

    private static RatingJournalEntry Game(int id, double daysAgo, double before, double after)
        => new(id, Now.AddDays(-daysAgo), before, 0, after, 0);

    [TestMethod]
    public void BoardsAreOrderedByGamesAndTheBusiestIsTheDefault()
    {
        PlayerRating[] ratings = [Rating("tmm_2v2", 45), Rating("global", 70), Rating("ladder_1v1", 200)];

        CollectionAssert.AreEqual(new[] { "ladder_1v1", "global", "tmm_2v2" }, ProfileSummaries.ByGames(ratings).Select(r => r.Board.TechnicalName).ToArray());
        Assert.AreEqual("ladder_1v1", ProfileSummaries.DefaultBoard(ratings));
        Assert.AreEqual("Ladder 1v1", ProfileSummaries.BoardOptions(ratings)[0].Label);
        Assert.AreEqual("tmm_2v2", ProfileSummaries.ActiveBoard(ratings, "tmm_2v2"));
        Assert.AreEqual("ladder_1v1", ProfileSummaries.ActiveBoard(ratings, "tmm_3v3"));
        Assert.AreEqual("ladder_1v1", ProfileSummaries.ActiveBoard(ratings, null));
        Assert.IsNull(ProfileSummaries.DefaultBoard([]));
    }

    [TestMethod]
    public void ChangeOverIsTheLastRatingMinusTheFirstRatingBefore()
    {
        RatingJournalEntry[] entries = [Game(3, 1, 1040, 1031), Game(1, 20, 1000, 1010), Game(2, 10, 1010, 1040)];

        Assert.AreEqual(31, ProfileSummaries.ChangeOver(entries));
        Assert.IsNull(ProfileSummaries.ChangeOver([]));
    }

    [TestMethod]
    public void AchievementsCountUnlockedOnesAndFindTheRarest()
    {
        AchievementDefinition[] definitions =
        [
            new("a", "Common", "", AchievementKind.Standard, 1, 1) { ExperiencePoints = 5, UnlockersPercent = 60 },
            new("b", "Rare", "", AchievementKind.Standard, 1, 2) { ExperiencePoints = 50, UnlockersPercent = 1.8 },
            new("c", "Locked", "", AchievementKind.Standard, 1, 3) { ExperiencePoints = 100, UnlockersPercent = 0.1 },
        ];
        PlayerAchievement[] mine =
        [
            new("a", AchievementState.Unlocked, 1, null),
            new("b", AchievementState.Unlocked, 1, null),
            new("c", AchievementState.Revealed, 0, null),
        ];

        AchievementSummary summary = ProfileSummaries.Achievements(definitions, mine);

        Assert.AreEqual(2, summary.Unlocked);
        Assert.AreEqual(3, summary.Total);
        Assert.AreEqual(55, summary.ExperiencePoints);
        Assert.AreEqual("Rare", summary.Rarest?.Name);
        Assert.IsNull(ProfileSummaries.Achievements(definitions, []).Rarest);
    }

    [TestMethod]
    public void LeagueLineUsesTheSeasonRunningOnTheBoard()
    {
        LeagueSeason season = new(9, 1, "ladder", 9, "season9", Now.AddDays(-30), Now.AddDays(30)) { BoardTechnicalName = "ladder_1v1", PlacementGames = 10 };
        LeagueSeason ended = new(8, 1, "ladder", 8, "season8", Now.AddDays(-90), Now.AddDays(-60)) { BoardTechnicalName = "ladder_1v1" };
        LeagueSubdivision silver3 = new(5, 3, "III", new LeagueDivision(2, 2, "Silver"));

        LeagueLine? placed = ProfileSummaries.LeagueOf("ladder_1v1", [season, ended], [new LeagueEntry(1, 9, 18, 40, false, silver3)], Now);
        LeagueLine? placing = ProfileSummaries.LeagueOf("ladder_1v1", [season], [new LeagueEntry(1, 9, 0, 3, false, null)], Now);

        Assert.AreEqual(new LeagueLine("Silver III", "18 points"), placed);
        Assert.AreEqual(new LeagueLine("Placement", "3 of 10 games"), placing);
        Assert.IsNull(ProfileSummaries.LeagueOf("tmm_2v2", [season], [new LeagueEntry(1, 9, 18, 40, false, silver3)], Now));
        Assert.IsNull(ProfileSummaries.LeagueOf("ladder_1v1", [ended], [new LeagueEntry(1, 8, 18, 40, false, silver3)], Now));
        Assert.IsNull(ProfileSummaries.LeagueOf("ladder_1v1", [season], [], Now));
    }

    [TestMethod]
    public void NameHistoryGivesEachNameItsSpan()
    {
        PlayerProfile profile = new(
            1,
            "Ravelin",
            new DateTimeOffset(2014, 3, 1, 12, 0, 0, TimeSpan.Zero),
            null,
            [
                new PlayerName("Rvln", new DateTimeOffset(2021, 3, 10, 12, 0, 0, TimeSpan.Zero)),
                new PlayerName("xXRavelinXx", new DateTimeOffset(2017, 6, 10, 12, 0, 0, TimeSpan.Zero)),
            ]);

        IReadOnlyList<NameSpan> names = ProfileSummaries.NameHistory(profile, TimeZoneInfo.Utc);

        CollectionAssert.AreEqual(
            new[] { "Ravelin|since Mar 2021|True", "Rvln|Jun 2017 to Mar 2021|False", "xXRavelinXx|Mar 2014 to Jun 2017|False" },
            names.Select(n => $"{n.Name}|{n.Span}|{n.IsCurrent}").ToArray());
        Assert.AreEqual(1, ProfileSummaries.NameHistory(new PlayerProfile(2, "Solo", null, null, []), TimeZoneInfo.Utc).Count);
    }
}

[TestClass]
public class RatingChartDataTest
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    private static RatingJournalEntry Game(int id, double daysAgo, double before, double after)
        => new(id, Now.AddDays(-daysAgo), before, 0, after, 0);

    [TestMethod]
    public void LoadSinceReachesBackThirtyDaysForAShortPeriod()
    {
        Assert.AreEqual(Now.AddDays(-30), RatingChartData.LoadSince(Now.AddDays(-7), Now));
        Assert.AreEqual(Now.AddDays(-365), RatingChartData.LoadSince(Now.AddDays(-365), Now));
        Assert.IsNull(RatingChartData.LoadSince(null, Now));
    }

    [TestMethod]
    public void WithinKeepsTheGamesOfThePeriodOldestFirst()
    {
        RatingJournalEntry[] entries = [Game(3, 1, 1020, 1030), Game(1, 40, 1000, 1010), Game(2, 5, 1010, 1020)];

        CollectionAssert.AreEqual(new[] { 2, 3 }, RatingChartData.Within(entries, Now.AddDays(-7)).Select(e => e.GameId).ToArray());
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, RatingChartData.Within(entries, null).Select(e => e.GameId).ToArray());
    }

    [TestMethod]
    public void PointsCarryTheEntryAndThePeakIsTheFirstHighest()
    {
        RatingJournalEntry[] entries = [Game(1, 30, 1000, 1010), Game(2, 20, 1010, 1050), Game(3, 10, 1050, 1050), Game(4, 5, 1050, 1020)];

        IReadOnlyList<LinePoint> points = RatingChartData.Points(entries);

        Assert.AreEqual(4, points.Count);
        Assert.AreSame(entries[1], points[1].Data);
        Assert.AreEqual(2, ((RatingJournalEntry)RatingChartData.Peak(points)!.Data!).GameId);
        Assert.IsNull(RatingChartData.Peak([]));
        Assert.AreEqual(4, RatingChartData.Sigma(entries).Count);
    }

    [TestMethod]
    public void BiggestGainAndLossAreTheEntries()
    {
        RatingJournalEntry[] entries = [Game(1, 30, 1000, 1010), Game(2, 20, 1010, 1040), Game(3, 10, 1040, 1010), Game(4, 5, 1010, 1010)];

        Assert.AreEqual(2, RatingChartData.BiggestGain(entries)?.GameId);
        Assert.AreEqual(3, RatingChartData.BiggestLoss(entries)?.GameId);
        Assert.IsNull(RatingChartData.BiggestLoss([Game(1, 1, 1000, 1010)]));
    }

    [TestMethod]
    public void SplitCountsAndAveragesLossesAndWins()
    {
        RatingJournalEntry[] entries = [Game(1, 4, 1000, 1010), Game(2, 3, 1010, 1020), Game(3, 2, 1020, 1000), Game(4, 1, 1000, 1000)];

        ChangeSplit split = RatingChartData.Split(entries);

        Assert.AreEqual(new ChangeSplit(1, -20, 2, 10), split);
        Assert.AreEqual(new ChangeSplit(0, null, 0, null), RatingChartData.Split([]));
    }

    [TestMethod]
    public void ChangeBinsSeparateLossesFromWinsAndLabelEverySecondEdge()
    {
        RatingJournalEntry[] entries = [Game(1, 4, 1000, 990), Game(2, 3, 990, 1010), Game(3, 2, 1010, 1022), Game(4, 1, 1022, 900)];

        IReadOnlyList<HistogramBin> bins = RatingChartData.ChangeBins(entries);

        Assert.AreEqual(14, bins.Count);
        Assert.AreEqual(1, bins[0].Value, "A fall of 122 lands in the first bin.");
        Assert.AreEqual(-1, bins[6].Sign);
        Assert.AreEqual(1, bins[7].Sign);
        Assert.AreEqual("−8", bins[5].Label);
        Assert.AreEqual("0", bins[7].Label);
        Assert.AreEqual("+8", bins[9].Label);
        Assert.AreEqual("", bins[8].Label);
        Assert.AreEqual("1", bins[0].TopLabel, "The tallest bin carries its count.");
    }

    [TestMethod]
    public void SparkValuesAreThinnedAndKeepTheEnds()
    {
        RatingJournalEntry[] entries = [.. Enumerable.Range(0, 500).Select(i => Game(i + 1, 500 - i, 1000 + i, 1001 + i))];

        IReadOnlyList<double> values = RatingChartData.SparkValues(entries, 80);

        Assert.AreEqual(80, values.Count);
        Assert.AreEqual(1001, values[0]);
        Assert.AreEqual(1500, values[^1]);
        Assert.AreEqual(3, RatingChartData.SparkValues(entries.Take(3).ToArray(), 80).Count);
    }

    [TestMethod]
    public void TablePageListsTheNewestGamesFirstAndClampsThePage()
    {
        RatingJournalEntry[] entries = [.. Enumerable.Range(1, 60).Select(i => Game(i, 100 - i, 1000, 1001))];

        IReadOnlyList<RatingJournalEntry> first = RatingChartData.TablePage(entries, 1, out int pageCount, out int shown);
        IReadOnlyList<RatingJournalEntry> last = RatingChartData.TablePage(entries, 99, out _, out int shownLast);

        Assert.AreEqual(3, pageCount);
        Assert.AreEqual(1, shown);
        Assert.AreEqual(25, first.Count);
        Assert.AreEqual(60, first[0].GameId);
        Assert.AreEqual(3, shownLast);
        Assert.AreEqual(10, last.Count);
        Assert.AreEqual(1, last[^1].GameId);
        Assert.AreEqual(0, RatingChartData.TablePage([], 1, out int emptyPages, out _).Count);
        Assert.AreEqual(1, emptyPages);
    }

    [TestMethod]
    public void CsvHasAHeaderOneRowPerGameAndQuotesWhatNeedsIt()
    {
        RatingJournalEntry[] entries =
        [
            new(1, new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero), 1500, 100, 1510, 90)
            {
                MapName = "Seton's Clutch",
                Opponents = [new PlayerReference(2, "Kestrel"), new PlayerReference(3, "Wren")],
            },
            new(2, new DateTimeOffset(2026, 10, 2, 8, 0, 0, TimeSpan.Zero), 1510, 90, 1480, 90) { MapName = "Fields, \"open\"" },
        ];

        string[] lines = RatingChartData.Csv(entries).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.AreEqual(3, lines.Length);
        Assert.AreEqual("Date,Game,Map,Opponents,Result,Rating before,Rating after,Change,Mean after,Deviation after", lines[0]);
        Assert.AreEqual("2026-10-02T08:00:00Z,2,\"Fields, \"\"open\"\"\",,Loss,1240,1210,-30.0,1480.0,90.0", lines[1]);
        Assert.AreEqual("2026-10-01T08:00:00Z,1,Seton's Clutch,Kestrel; Wren,Win,1200,1240,40.0,1510.0,90.0", lines[2]);
    }

    [TestMethod]
    public void CsvDataUrlEscapesTheText()
    {
        string url = RatingChartData.CsvDataUrl("a,b\r\n");

        Assert.AreEqual("data:text/csv;charset=utf-8,%EF%BB%BFa%2Cb%0D%0A", url);
    }
}
