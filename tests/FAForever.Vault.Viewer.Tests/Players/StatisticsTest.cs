using FAForever.Vault.Viewer.Features.Players.Army;
using FAForever.FileFormats.Blueprints;
using FAForever.Vault.Viewer.Services.Players;
using FAForever.Vault.Viewer.Services.Players.Statistics;

namespace FAForever.Vault.Viewer.Tests.Players;

[TestClass]
public class RatingStatisticsTest
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A game with no deviation, so the mean is the rating.</summary>
    private static RatingJournalEntry Game(int id, double daysAgo, double before, double after)
        => new(id, Now.AddDays(-daysAgo), before, 0, after, 0);

    private static RatingJournalEntry[] Journal() =>
    [
        Game(1, 40, 1000, 1010),
        Game(2, 35, 1010, 1030),
        Game(3, 20, 1030, 1012),
        Game(4, 10, 1012, 1012),
        Game(5, 5, 1012, 1040),
        Game(6, 1, 1040, 1031),
    ];

    [TestMethod]
    public void SummaryOfAKnownJournal()
    {
        RatingSummary summary = RatingStatistics.Summarize(Journal(), Now);

        Assert.AreEqual(1031, summary.Current);
        Assert.AreEqual(1040, summary.Peak);
        Assert.AreEqual(Now.AddDays(-5), summary.PeakDate);
        Assert.AreEqual(1, summary.Change30Days, "1031 now, 1030 before the first game of the last 30 days");
        Assert.AreEqual(6, summary.Games);
        Assert.AreEqual(3, summary.Wins);
        Assert.AreEqual(2, summary.Losses, "a game that did not move the rating is neither");
        Assert.AreEqual(31.0 / 6, summary.AverageChange!.Value, 1e-9);
        Assert.AreEqual(28.0, summary.BiggestGain!.Value, 1e-9);
        Assert.AreEqual(-18.0, summary.BiggestLoss!.Value, 1e-9);
    }

    [TestMethod]
    public void SummaryDoesNotDependOnTheOrder()
    {
        RatingJournalEntry[] reversed = [.. Journal().Reverse()];
        Assert.AreEqual(RatingStatistics.Summarize(Journal(), Now), RatingStatistics.Summarize(reversed, Now));
    }

    [TestMethod]
    public void SummaryUsesTheDisplayRating()
    {
        // 1500 - 3 x 100 = 1200 before, 1520 - 3 x 90 = 1250 after
        RatingSummary summary = RatingStatistics.Summarize([new RatingJournalEntry(1, Now.AddDays(-1), 1500, 100, 1520, 90)], Now);

        Assert.AreEqual(1250, summary.Current);
        Assert.AreEqual(50, summary.Change30Days);
        Assert.AreEqual(50.0, summary.BiggestGain!.Value, 1e-9);
        Assert.IsNull(summary.BiggestLoss);
    }

    [TestMethod]
    public void SummaryOfNothing()
    {
        RatingSummary summary = RatingStatistics.Summarize([], Now);

        Assert.IsNull(summary.Current);
        Assert.IsNull(summary.Peak);
        Assert.IsNull(summary.Change30Days);
        Assert.AreEqual(0, summary.Games);
        Assert.IsNull(summary.AverageChange);
    }

    [TestMethod]
    public void NoGamesInThirtyDaysMeansNoChange()
    {
        RatingSummary summary = RatingStatistics.Summarize([Game(1, 90, 1000, 1010), Game(2, 60, 1010, 1020)], Now);

        Assert.AreEqual(1020, summary.Current);
        Assert.IsNull(summary.Change30Days);
    }

    [TestMethod]
    public void HistogramHasFourteenBinsOfFour()
    {
        IReadOnlyList<HistogramBin> bins = RatingStatistics.ChangeHistogram(Journal());

        Assert.AreEqual(14, bins.Count);
        Assert.AreEqual(-28, bins[0].From);
        Assert.AreEqual(-24, bins[0].To);
        Assert.AreEqual(24, bins[^1].From);
        Assert.AreEqual(28, bins[^1].To);
        Assert.AreEqual(6, bins.Sum(bin => bin.Count));

        // changes: +10, +20, -18, 0, +28, -9
        Assert.AreEqual(1, bins[9].Count, "+10 is in [8, 12)");
        Assert.AreEqual(1, bins[12].Count, "+20 is in [20, 24)");
        Assert.AreEqual(1, bins[2].Count, "-18 is in [-20, -16)");
        Assert.AreEqual(1, bins[7].Count, "0 is in [0, 4)");
        Assert.AreEqual(1, bins[13].Count, "+28 goes in the last bin");
        Assert.AreEqual(1, bins[4].Count, "-9 is in [-12, -8)");
    }

    [TestMethod]
    public void HistogramPutsTheExtremesInTheOuterBins()
    {
        IReadOnlyList<HistogramBin> bins = RatingStatistics.ChangeHistogram([Game(1, 3, 1000, 950), Game(2, 2, 1000, 1100), Game(3, 1, 1000, 998)]);

        Assert.AreEqual(1, bins[0].Count);
        Assert.AreEqual(1, bins[13].Count);
        Assert.AreEqual(1, bins[6].Count, "-2 is in [-4, 0)");
    }

    [TestMethod]
    public void SeriesIsOldestFirstAndSkipsUndatedGames()
    {
        RatingJournalEntry undated = new(9, null, 1000, 0, 1005, 0);
        IReadOnlyList<RatingPoint> series = RatingStatistics.Series([.. Journal().Reverse(), undated]);

        Assert.AreEqual(6, series.Count);
        CollectionAssert.AreEqual(new double[] { 1010, 1030, 1012, 1012, 1040, 1031 }, series.Select(point => point.Rating).ToArray());
        Assert.AreEqual(1, series[0].GameId);
    }

    [TestMethod]
    public void SeriesPerBoardKeepsTheBoards()
    {
        IReadOnlyDictionary<string, IReadOnlyList<RatingPoint>> boards = RatingStatistics.SeriesPerBoard(new Dictionary<string, IReadOnlyList<RatingJournalEntry>>
        {
            ["ladder_1v1"] = Journal(),
            ["tmm_2v2"] = [Game(7, 2, 900, 910)],
        });

        Assert.AreEqual(6, boards["ladder_1v1"].Count);
        Assert.AreEqual(910, boards["tmm_2v2"][0].Rating);
    }
}

[TestClass]
public class MatchStatisticsTest
{
    private static readonly DateTimeOffset Monday3am = new(2026, 10, 12, 3, 0, 0, TimeSpan.Zero);

    private static PlayerMatch Match(int id, MatchResult result, Faction? faction, string? map, double? minutes, int? before = null, int? after = null, DateTimeOffset? start = null)
        => new(id, $"Game {id}", start, result)
        {
            Faction = faction,
            MapName = map,
            Duration = minutes is { } length ? TimeSpan.FromMinutes(length) : null,
            RatingBefore = before,
            RatingAfter = after,
        };

    private static PlayerMatch[] Matches() =>
    [
        Match(1, MatchResult.Victory, Faction.Cybran, "Alpha", 12, 1000, 1010, Monday3am),
        Match(2, MatchResult.Defeat, Faction.Cybran, "Alpha", 25, 1010, 1002, new DateTimeOffset(2026, 10, 18, 23, 30, 0, TimeSpan.Zero)),
        Match(3, MatchResult.Victory, Faction.Aeon, "Beta", 31, 1002, 1007, new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero)),
        Match(4, MatchResult.Draw, Faction.Uef, "Alpha", 50),
        Match(5, MatchResult.Defeat, Faction.Seraphim, "Gamma", 9),
        Match(6, MatchResult.Victory, null, null, null),
    ];

    [TestMethod]
    public void Summary()
    {
        MatchSummary summary = MatchStatistics.Summarize(Matches());

        Assert.AreEqual(6, summary.Games);
        Assert.AreEqual(3, summary.Wins);
        Assert.AreEqual(2, summary.Losses);
        Assert.AreEqual(1, summary.Draws);
        Assert.AreEqual(0.6, summary.WinShare!.Value, 1e-9);
        Assert.AreEqual(25.4, summary.AverageLength!.Value.TotalMinutes, 1e-6);
        Assert.AreEqual(7.0 / 3, summary.AverageRatingChange!.Value, 1e-9);
        Assert.AreEqual("Alpha", summary.MostPlayedMap);
        Assert.AreEqual(3, summary.MostPlayedMapGames);
    }

    [TestMethod]
    public void SummaryOfNothing()
    {
        MatchSummary summary = MatchStatistics.Summarize([]);

        Assert.AreEqual(0, summary.Games);
        Assert.IsNull(summary.WinShare);
        Assert.IsNull(summary.AverageLength);
        Assert.IsNull(summary.AverageRatingChange);
        Assert.IsNull(summary.MostPlayedMap);
    }

    [TestMethod]
    public void ByFactionListsAllFourInTheGamesOrder()
    {
        IReadOnlyList<FactionRecord> factions = MatchStatistics.ByFaction(Matches());

        CollectionAssert.AreEqual(new[] { Faction.Uef, Faction.Aeon, Faction.Cybran, Faction.Seraphim }, factions.Select(record => record.Faction).ToArray());
        CollectionAssert.AreEqual(new[] { 1, 1, 2, 1 }, factions.Select(record => record.Games).ToArray());
        CollectionAssert.AreEqual(new[] { 0, 1, 1, 0 }, factions.Select(record => record.Wins).ToArray());
        CollectionAssert.AreEqual(new[] { 0, 0, 1, 1 }, factions.Select(record => record.Losses).ToArray());
        Assert.IsNull(factions[0].WinShare, "a draw is neither a win nor a loss");
        Assert.AreEqual(0.5, factions[2].WinShare!.Value, 1e-9);
        Assert.AreEqual(1.0, factions[1].WinShare!.Value, 1e-9);
    }

    [TestMethod]
    public void ByMapCountsGamesAndKeepsTheTop()
    {
        IReadOnlyList<MapRecord> maps = MatchStatistics.ByMap(Matches(), 2);

        CollectionAssert.AreEqual(new[] { "Alpha", "Beta" }, maps.Select(record => record.Map).ToArray());
        Assert.AreEqual(3, maps[0].Games);
        Assert.AreEqual(1, maps[0].Wins);
        Assert.AreEqual(1, maps[0].Losses);
        Assert.AreEqual(3, MatchStatistics.ByMap(Matches()).Count, "the game without a map is left out");
    }

    [TestMethod]
    public void ByLengthHasFiveBuckets()
    {
        IReadOnlyList<LengthBucket> buckets = MatchStatistics.ByLength(Matches());

        CollectionAssert.AreEqual(new[] { 0, 10, 20, 30, 45 }, buckets.Select(bucket => bucket.FromMinutes).ToArray());
        CollectionAssert.AreEqual(new int?[] { 10, 20, 30, 45, null }, buckets.Select(bucket => bucket.ToMinutes).ToArray());
        CollectionAssert.AreEqual(new[] { 1, 1, 1, 1, 1 }, buckets.Select(bucket => bucket.Games).ToArray());
        CollectionAssert.AreEqual(new[] { 0, 1, 0, 1, 0 }, buckets.Select(bucket => bucket.Wins).ToArray());
        CollectionAssert.AreEqual(new[] { 1, 0, 1, 0, 0 }, buckets.Select(bucket => bucket.Losses).ToArray());
    }

    [TestMethod]
    public void BucketEdgesBelongToTheLongerBucket()
    {
        IReadOnlyList<LengthBucket> buckets = MatchStatistics.ByLength([Match(1, MatchResult.Victory, null, null, 10), Match(2, MatchResult.Victory, null, null, 45)]);

        CollectionAssert.AreEqual(new[] { 0, 1, 0, 0, 1 }, buckets.Select(bucket => bucket.Games).ToArray());
    }

    [TestMethod]
    public void HeatmapCountsWeekdayAndFourHourBlockInTheGivenZone()
    {
        IReadOnlyList<IReadOnlyList<int>> utc = MatchStatistics.Heatmap(Matches(), TimeZoneInfo.Utc);

        Assert.AreEqual(7, utc.Count);
        Assert.IsTrue(utc.All(day => day.Count == 6));
        Assert.AreEqual(3, utc.Sum(day => day.Sum()), "three games have a start time");
        Assert.AreEqual(1, utc[0][0], "Monday 03:00");
        Assert.AreEqual(1, utc[6][5], "Sunday 23:30");
        Assert.AreEqual(1, utc[4][3], "Friday 12:00");

        TimeZoneInfo plusTwo = TimeZoneInfo.CreateCustomTimeZone("plus-two", TimeSpan.FromHours(2), "plus two", "plus two");
        IReadOnlyList<IReadOnlyList<int>> shifted = MatchStatistics.Heatmap(Matches(), plusTwo);
        Assert.AreEqual(1, shifted[0][1], "Monday 05:00");
        Assert.AreEqual(1, shifted[0][0], "Sunday 23:30 UTC is Monday 01:30");
        Assert.AreEqual(1, shifted[4][3], "Friday 14:00");
    }
}

[TestClass]
public class ArmyStatisticsTest
{
    private static PlayerEventCounts Counts(params (string Id, long Count)[] counts)
        => new(counts.ToDictionary(count => count.Id, count => count.Count));

    [TestMethod]
    public void SummaryFromCounters()
    {
        ArmySummary summary = ArmyStatistics.Summarize(Counts(
            (PlayerEvents.UefPlays, 10), (PlayerEvents.UefWins, 6), (PlayerEvents.SeraphimPlays, 3),
            (PlayerEvents.CustomGamesPlayed, 4), (PlayerEvents.LadderGamesPlayed, 6), (PlayerEvents.AcusLost, 5),
            (PlayerEvents.SecondsPlayed, 7200),
            (PlayerEvents.BuiltAir, 30), (PlayerEvents.LostAir, 12), (PlayerEvents.BuiltNaval, 7),
            (PlayerEvents.BuiltTech2, 400), (PlayerEvents.LostTech2, 150), (PlayerEvents.BuiltExperimentals, 9),
            (PlayerEvents.BuiltEngineers, 80), (PlayerEvents.LostEngineers, 20)));

        CollectionAssert.AreEqual(new[] { Faction.Uef, Faction.Aeon, Faction.Cybran, Faction.Seraphim }, summary.Factions.Select(faction => faction.Faction).ToArray());
        Assert.AreEqual(10, summary.Factions[0].Plays);
        Assert.AreEqual(6, summary.Factions[0].Wins);
        Assert.AreEqual(4, summary.Factions[0].Losses);
        Assert.AreEqual(0.6, summary.Factions[0].WinShare!.Value, 1e-9);
        Assert.IsNull(summary.Factions[1].WinShare, "Aeon was never played");
        Assert.AreEqual(0, summary.Factions[3].Wins);
        Assert.AreEqual(4, summary.CustomGames);
        Assert.AreEqual(6, summary.LadderGames);
        Assert.AreEqual(2.0, summary.HoursPlayed!.Value, 1e-9);
        Assert.AreEqual(5, summary.AcusLost);
        Assert.AreEqual(0.5, summary.AcusLostPerGame!.Value, 1e-9);

        CollectionAssert.AreEqual(new[] { "Air", "Land", "Naval" }, summary.ByCategory.Select(group => group.Label).ToArray());
        Assert.AreEqual(new BuiltAndLost("Air", 30, 12), summary.ByCategory[0]);
        Assert.AreEqual(new BuiltAndLost("Naval", 7, 0), summary.ByCategory[2]);
        CollectionAssert.AreEqual(new[] { "Tech 1", "Tech 2", "Tech 3", "Experimental" }, summary.ByTech.Select(group => group.Label).ToArray());
        Assert.AreEqual(new BuiltAndLost("Tech 2", 400, 150), summary.ByTech[1]);
        Assert.AreEqual(9, summary.ByTech[3].Built);
        Assert.AreEqual(new BuiltAndLost("Engineers", 80, 20), summary.Engineers);
    }

    [TestMethod]
    public void WithoutGamesThereIsNoRate()
    {
        ArmySummary summary = ArmyStatistics.Summarize(Counts((PlayerEvents.AcusLost, 3)));

        Assert.IsNull(summary.AcusLostPerGame);
        Assert.IsNull(summary.HoursPlayed);
        Assert.IsNull(summary.CustomGames);
        Assert.IsNull(summary.LadderGames);
    }

    [TestMethod]
    public void AbsentCountersAreNullAndZeroIsKept()
    {
        PlayerEventCounts none = Counts((PlayerEvents.UefPlays, 30), (PlayerEvents.CybranPlays, 20), (PlayerEvents.AcusLost, 10));
        Assert.IsFalse(none.Has(PlayerEvents.SecondsPlayed));
        Assert.IsNull(none.TryGet(PlayerEvents.SecondsPlayed));

        ArmySummary summary = ArmyStatistics.Summarize(none);
        Assert.IsNull(summary.HoursPlayed);
        Assert.IsNull(summary.CustomGames);
        Assert.AreEqual(0.2, summary.AcusLostPerGame!.Value, 1e-9, "per faction game when the game counters are absent");
        Assert.IsFalse(ArmyFormat.HasModeCounters(summary));
        Assert.AreEqual(8421, ArmyFormat.TotalGames(8421, summary), "the match list total wins");
        Assert.AreEqual(50, ArmyFormat.TotalGames(null, summary));

        PlayerEventCounts zero = Counts((PlayerEvents.SecondsPlayed, 0), (PlayerEvents.CustomGamesPlayed, 0));
        Assert.IsTrue(zero.Has(PlayerEvents.SecondsPlayed));
        Assert.AreEqual(0.0, ArmyStatistics.Summarize(zero).HoursPlayed);
    }

    [TestMethod]
    public void MoreWinsThanPlaysDoesNotGiveNegativeLosses()
        => Assert.AreEqual(0, ArmyStatistics.Summarize(Counts((PlayerEvents.AeonPlays, 2), (PlayerEvents.AeonWins, 5))).Factions[1].Losses);

    [TestMethod]
    public void EventIdsAreUniqueLowerCaseGuids()
    {
        Assert.AreEqual(28, PlayerEvents.All.Count);
        Assert.AreEqual(28, PlayerEvents.All.Distinct().Count());
        Assert.IsTrue(PlayerEvents.All.All(id => Guid.TryParse(id, out _) && id == id.ToLowerInvariant()));
    }
}

[TestClass]
public class LeaderboardsTest
{
    [TestMethod]
    [DataRow("global", "Global")]
    [DataRow("ladder_1v1", "Ladder 1v1")]
    [DataRow("tmm_2v2", "2v2")]
    [DataRow("tmm_4v4_full_share", "4v4 full share")]
    [DataRow("tmm_4v4_share_until_death", "4v4 share until death")]
    [DataRow("tmm_3v3", "3v3")]
    [DataRow("tmm_5v5_no_share", "5v5 no share")]
    [DataRow("some_board", "Some board")]
    [DataRow("tmm_", "tmm_")]
    public void DisplayName(string technicalName, string expected)
        => Assert.AreEqual(expected, Leaderboards.DisplayName(technicalName));
}

[TestClass]
public class PeriodsTest
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    [DataRow("1w", 2026, 10, 9)]
    [DataRow("1m", 2026, 9, 16)]
    [DataRow("3m", 2026, 7, 16)]
    [DataRow("1y", 2025, 10, 16)]
    public void SinceIsTheStartOfThePeriod(string period, int year, int month, int day)
        => Assert.AreEqual(new DateTimeOffset(year, month, day, 12, 0, 0, TimeSpan.Zero), Periods.Since(period, Now));

    [TestMethod]
    public void AllHasNoStart() => Assert.IsNull(Periods.Since("all", Now));

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("2y")]
    [DataRow("1W")]
    public void AnUnknownPeriodIsAYear(string? period)
    {
        Assert.AreEqual("1y", Periods.Normalize(period));
        Assert.AreEqual(Now.AddYears(-1), Periods.Since(period, Now));
    }

    [TestMethod]
    public void EveryPeriodIsNormalizedToItself()
        => Assert.IsTrue(Periods.Values.All(period => Periods.Normalize(period) == period) && Periods.Values.Count == 5);
}

[TestClass]
public class RankStatisticsTest
{
    [TestMethod]
    [DataRow(0, 1)]
    [DataRow(1, 2)]
    [DataRow(99, 100)]
    [DataRow(-5, 1)]
    public void RankIsOneMoreThanTheBetterRatings(int better, int rank)
        => Assert.AreEqual(rank, RankStatistics.RankFromCount(better));

    [TestMethod]
    public void TopShare()
    {
        Assert.AreEqual(0.1, RankStatistics.TopShare(10, 100)!.Value, 1e-9);
        Assert.AreEqual(1.0, RankStatistics.TopShare(500, 100)!.Value, 1e-9);
        Assert.IsNull(RankStatistics.TopShare(1, 0));
    }
}

[TestClass]
public class LeagueNamesTest
{
    [TestMethod]
    [DataRow("divisions.silver.name", "Silver")]
    [DataRow("divisions.silver", "Silver")]
    [DataRow("seasons.ladder_1v1.3.name", "Ladder 1v1")]
    [DataRow("x.gold_tier", "Gold tier")]
    [DataRow("master", "Master")]
    public void FromKeyTakesTheLastWordSegment(string key, string expected)
        => Assert.AreEqual(expected, LeagueNames.FromKey(key));

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("name")]
    [DataRow("a.1.name")]
    public void FromKeyCanFindNothing(string? key)
    {
        if (key is "a.1.name")
        {
            Assert.AreEqual("A", LeagueNames.FromKey(key));
        }
        else
        {
            Assert.IsNull(LeagueNames.FromKey(key));
        }
    }

    [TestMethod]
    public void DivisionFallsBackToItsIndex()
    {
        Assert.AreEqual("Silver", LeagueNames.Division("divisions.silver.name", 2));
        Assert.AreEqual("3", LeagueNames.Division(null, 3));
    }

    [TestMethod]
    [DataRow(1, "I")]
    [DataRow(4, "IV")]
    [DataRow(5, "V")]
    [DataRow(9, "IX")]
    [DataRow(14, "XIV")]
    [DataRow(40, "XL")]
    [DataRow(1994, "MCMXCIV")]
    [DataRow(3999, "MMMCMXCIX")]
    [DataRow(0, "0")]
    [DataRow(4000, "4000")]
    public void Roman(int number, string expected)
    {
        Assert.AreEqual(expected, LeagueNames.Roman(number));
        Assert.AreEqual(expected, LeagueNames.Subdivision("whatever.key", number));
    }
}

[TestClass]
public class LeagueStatisticsTest
{
    private static LeagueSubdivision Sub(int id, int division, int index)
        => new(id, index, LeagueNames.Roman(index), new LeagueDivision(division * 10, division, "D" + division));

    [TestMethod]
    public void MovesCountPromotionsAndDemotions()
    {
        LeagueSubdivision s1 = Sub(1, 1, 3);
        LeagueSubdivision s2 = Sub(2, 2, 1);
        LeagueSubdivision s3 = Sub(3, 2, 2);

        (int promotions, int demotions) = LeagueStatistics.Moves(
        [
            new LeagueJournalEntry(1, 0, 0, 1, null, null),
            new LeagueJournalEntry(2, 0, 0, 2, null, s1),
            new LeagueJournalEntry(3, 5, 6, 3, s1, s1),
            new LeagueJournalEntry(4, 9, 0, 4, s1, s2),
            new LeagueJournalEntry(5, 4, 0, 5, s2, s3),
            new LeagueJournalEntry(6, 1, 0, 6, s3, s1),
        ]);

        Assert.AreEqual(2, promotions, "entering from placement is not a promotion");
        Assert.AreEqual(1, demotions);
    }

    [TestMethod]
    public void NoJournalNoMoves() => Assert.AreEqual((0, 0), LeagueStatistics.Moves([]));
}
