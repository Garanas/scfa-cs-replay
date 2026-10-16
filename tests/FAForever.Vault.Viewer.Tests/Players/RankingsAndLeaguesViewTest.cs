using FAForever.Vault.Viewer.Features.Players.Charts;
using FAForever.Vault.Viewer.Features.Players.Leagues;
using FAForever.Vault.Viewer.Features.Players.Rankings;
using FAForever.Vault.Viewer.Services.Players;

namespace FAForever.Vault.Viewer.Tests.Players;

[TestClass]
public class RankingViewTest
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 34, 56, TimeSpan.Zero);

    private static IReadOnlyList<RatingBin> Bins(params int[] counts)
        => [.. counts.Select((count, index) => new RatingBin(index * 100, (index + 1) * 100, count))];

    [TestMethod]
    public void NoParametersGiveTheDefaults()
    {
        RankingQuery query = RankingView.ToQuery(null, null, null, null, null, Now);

        Assert.AreEqual("ladder_1v1", query.Board);
        Assert.IsTrue(query.SettledOnly);
        Assert.AreEqual(new DateTimeOffset(2026, 7, 18, 0, 0, 0, TimeSpan.Zero), query.ActiveSince);
        Assert.IsNull(query.Name);
        Assert.AreEqual(1, query.Page);
        Assert.AreEqual(50, query.PageSize);
    }

    [TestMethod]
    public void ParametersChangeTheQuery()
    {
        RankingQuery query = RankingView.ToQuery("global", "0", "all", "  ash ", "3", Now);

        Assert.AreEqual("global", query.Board);
        Assert.IsFalse(query.SettledOnly);
        Assert.IsNull(query.ActiveSince);
        Assert.AreEqual("ash", query.Name);
        Assert.AreEqual(3, query.Page);
    }

    [TestMethod]
    public void TheSameChoiceGivesTheSameQueryAllDay()
        => Assert.AreEqual(RankingView.ToQuery(null, null, "30d", null, null, Now), RankingView.ToQuery(null, null, "30d", null, null, Now.AddHours(8)));

    [TestMethod]
    [DataRow("30d", 30)]
    [DataRow("90d", 90)]
    [DataRow("nonsense", 90)]
    public void ActivityCountsBackFromToday(string token, int days)
        => Assert.AreEqual(new DateTimeOffset(Now.UtcDateTime.Date, TimeSpan.Zero).AddDays(-days), RankingView.ActiveSince(RankingView.Activity(token), Now));

    [TestMethod]
    [DataRow("../x")]
    [DataRow("a b")]
    [DataRow("")]
    public void AnUnusableBoardOpensTheLadder(string token)
        => Assert.AreEqual("ladder_1v1", RankingView.Board(token));

    [TestMethod]
    [DataRow("0")]
    [DataRow("-4")]
    [DataRow("x")]
    public void AnUnusablePageIsTheFirst(string token)
        => Assert.AreEqual(1, RankingView.PageNumber(token));

    [TestMethod]
    [DataRow(1, 1)]
    [DataRow(50, 1)]
    [DataRow(51, 2)]
    [DataRow(312, 7)]
    [DataRow(0, 1)]
    public void ARankLiesOnItsPage(int rank, int page)
        => Assert.AreEqual(page, RankingView.PageOfRank(rank));

    [TestMethod]
    public void MedianIsInterpolatedInsideItsBin()
    {
        // 100 ratings: 20 in 0 to 100, 60 in 100 to 200, 20 in 200 to 300. The 50th lies a half of the way into the middle bin.
        Assert.AreEqual(150, RankingView.Median(Bins(20, 60, 20)));
        Assert.IsNull(RankingView.Median(Bins(0, 0, 0)));
    }

    [TestMethod]
    public void CountsAtOrAboveAThreshold()
    {
        IReadOnlyList<RatingBin> bins = Bins(10, 20, 30, 40);

        Assert.AreEqual(70, RankingView.CountAtLeast(bins, 200));
        Assert.AreEqual(90, RankingView.CountAtLeast(bins, 100));
        Assert.AreEqual(80, RankingView.CountAtLeast(bins, 150));
        Assert.AreEqual(0, RankingView.CountAtLeast(bins, 400));
    }

    [TestMethod]
    public void ThePlayersBinIsPrimaryAndTheRestFaint()
    {
        IReadOnlyList<ColumnItem> columns = RankingView.Columns(Bins(1, 2, 3, 4), 250);

        Assert.AreEqual(4, columns.Count);
        Assert.AreEqual(ChartColor.Primary, columns[2].Color);
        Assert.AreEqual(3, columns.Count(column => column.Color == ChartColor.Faint));
        Assert.IsTrue(columns.All(column => column.Label == ""));
    }

    [TestMethod]
    public void NoRatingMarksNoBin()
        => Assert.IsTrue(RankingView.Columns(Bins(1, 2), null).All(column => column.Color == ChartColor.Faint));

    [TestMethod]
    public void RatingsBeyondTheBinsLandInTheEnds()
    {
        IReadOnlyList<RatingBin> bins = Bins(1, 1, 1);

        Assert.AreEqual(0, RankingView.BinIndex(bins, -40));
        Assert.AreEqual(2, RankingView.BinIndex(bins, 9000));
    }

    [TestMethod]
    public void PointsToTheTopOnlyWhileBelowIt()
    {
        Assert.AreEqual(216, RankingView.PointsToReach(1804, 1588));
        Assert.IsNull(RankingView.PointsToReach(1804, 1900));
        Assert.IsNull(RankingView.PointsToReach(null, 1900));
    }
}

[TestClass]
public class LeagueViewTest
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 0, 0, 0, TimeSpan.Zero);

    private static readonly LeagueDivision Bronze = new(1, 1, "Bronze");
    private static readonly LeagueDivision Silver = new(2, 2, "Silver");

    private static readonly LeagueSubdivision[] Subdivisions =
    [
        new(10, 1, "I", Bronze),
        new(11, 2, "II", Bronze),
        new(20, 1, "I", Silver),
        new(21, 2, "II", Silver),
    ];

    private static readonly League[] Leagues = [new(2, "other", "leagues.other.name"), new(1, "ladder_1v1", "leagues.ladder_1v1.name")];

    private static readonly LeagueSeason[] Seasons =
    [
        new(7, 1, "ladder_1v1", 1, "s1", Now.AddDays(-300), Now.AddDays(-200)),
        new(9, 1, "ladder_1v1", 3, "s3", Now.AddDays(-10), Now.AddDays(100)),
        new(8, 1, "ladder_1v1", 2, "s2", Now.AddDays(-200), Now.AddDays(-10)),
        new(30, 2, "other", 1, "o1", Now.AddDays(-400), Now.AddDays(-300)),
    ];

    private static LeagueJournalEntry Game(int count, int? before, int? after, int score = 0)
        => new(count, 0, score, count, before is { } b ? Subdivisions[b] : null, after is { } a ? Subdivisions[a] : null);

    [TestMethod]
    public void TheLadderLeagueOpensWithoutAChoice()
    {
        Assert.AreEqual("ladder_1v1", LeagueView.ChooseLeague(Leagues, null)!.TechnicalName);
        Assert.AreEqual("other", LeagueView.ChooseLeague(Leagues, "other")!.TechnicalName);
        Assert.AreEqual("ladder_1v1", LeagueView.ChooseLeague(Leagues, "gone")!.TechnicalName);
        Assert.IsNull(LeagueView.ChooseLeague([], null));
    }

    [TestMethod]
    public void TheRunningSeasonOpensWithoutAChoice()
    {
        Assert.AreEqual(9, LeagueView.ChooseSeason(Seasons, 1, null, Now)!.Id);
        Assert.AreEqual(8, LeagueView.ChooseSeason(Seasons, 1, "8", Now)!.Id);
    }

    [TestMethod]
    public void ASeasonOfAnotherLeagueOrNoneRunningFallsBack()
    {
        Assert.AreEqual(9, LeagueView.ChooseSeason(Seasons, 1, "30", Now)!.Id);
        Assert.AreEqual(30, LeagueView.ChooseSeason(Seasons, 2, null, Now)!.Id); // none running: the latest
    }

    [TestMethod]
    public void TheSubdivisionOpensOnTheOwnElseTheTop()
    {
        Assert.AreEqual(20, LeagueView.ChooseSubdivision(Subdivisions, null, Subdivisions[2])!.Id);
        Assert.AreEqual(21, LeagueView.ChooseSubdivision(Subdivisions, null, null)!.Id);
        Assert.AreEqual(11, LeagueView.ChooseSubdivision(Subdivisions, "11", Subdivisions[2])!.Id);
        Assert.AreEqual(20, LeagueView.ChooseSubdivision(Subdivisions, "999", Subdivisions[2])!.Id);
    }

    [TestMethod]
    public void TheJourneySkipsPlacementAndFlagsPromotions()
    {
        IReadOnlyList<JourneyPoint> points = LeagueView.Journey(
        [
            Game(3, 1, 2, 12),
            Game(1, null, null),
            Game(2, null, 1, 5),
            Game(4, 2, 1, 3),
            Game(5, 1, 1, 4),
        ], Subdivisions);

        CollectionAssert.AreEqual(new[] { 2, 3, 4, 5 }, points.Select(point => point.Game).ToArray());
        CollectionAssert.AreEqual(new[] { 1, 2, 1, 1 }, points.Select(point => point.Position).ToArray());
        CollectionAssert.AreEqual(new[] { false, true, false, false }, points.Select(point => point.Promoted).ToArray());
    }

    [TestMethod]
    public void GroupsFollowTheDivisionsWithTheirTotals()
    {
        IReadOnlyList<DivisionGroup> groups = LeagueView.Groups(Subdivisions, new Dictionary<int, int> { [10] = 5, [11] = 7, [20] = 1 });

        Assert.AreEqual(2, groups.Count);
        Assert.AreEqual("Bronze", groups[0].Division.Name);
        Assert.AreEqual(12, groups[0].Total);
        Assert.AreEqual(1, groups[1].Total);
        Assert.AreEqual(0, groups[1].Subdivisions[1].Players);
    }

    [TestMethod]
    public void PlacementDependsOnReturning()
    {
        LeagueSeason season = Seasons[0] with { PlacementGames = 10, PlacementGamesReturningPlayer = 3 };

        Assert.AreEqual(10, LeagueView.PlacementGames(season, false));
        Assert.AreEqual(3, LeagueView.PlacementGames(season, true));
    }
}
