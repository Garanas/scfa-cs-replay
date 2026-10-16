using FAForever.Vault.Viewer.Features.Players;
using FAForever.Vault.Viewer.Features.Players.Charts;
using FAForever.Vault.Viewer.Features.Players.Leagues;
using FAForever.Vault.Viewer.Features.Players.Rankings;
using FAForever.Vault.Viewer.Services.Players;

namespace FAForever.Vault.Viewer.Tests.Players;

[TestClass]
public class ShellViewTest
{
    private static readonly string[] Boards = ["global", "ladder_1v1", "tmm_2v2"];

    [TestMethod]
    public void TheBoardChipsListTheSixLiveBoardsInOrder()
        => CollectionAssert.AreEqual(
            new[] { "global", "ladder_1v1", "tmm_2v2", "tmm_3v3", "tmm_4v4_full_share", "tmm_4v4_share_until_death" },
            PlayerFilters.Boards.Select(option => option.Token).ToArray());

    [TestMethod]
    public void CleanParametersAreNotStale()
        => Assert.AreEqual(0, RankingView.StaleParameters("global", "0", "30d", "3", Boards).Count);

    [TestMethod]
    public void UnknownValuesAreStale()
    {
        IReadOnlyList<string> stale = RankingView.StaleParameters("zzz", "yes", "zz", "abc", Boards);

        CollectionAssert.AreEqual(new[] { "board", "settled", "active", "page" }, stale.ToArray());
    }

    [TestMethod]
    [DataRow("0")]
    [DataRow("1")]
    [DataRow("-4")]
    public void APageBelowTwoIsStale(string page)
        => CollectionAssert.AreEqual(new[] { "page" }, RankingView.StaleParameters(null, null, null, page, Boards).ToArray());

    [TestMethod]
    public void TheDefaultPlayedInIsStaleInTheAddress()
        => CollectionAssert.AreEqual(new[] { "active" }, RankingView.StaleParameters(null, null, "90d", null, Boards).ToArray());

    [TestMethod]
    [DataRow(null, 8423, 50, 169)]
    [DataRow(7, null, 50, 1)]
    [DataRow(null, 0, 50, 1)]
    [DataRow(null, 100, 50, 2)]
    [DataRow(null, null, 50, 1)]
    public void TheLastPageComesFromTheCountsOrTheTotal(int? pages, int? total, int size, int expected)
        => Assert.AreEqual(pages ?? expected, RankingView.LastPage(pages, total, size));

    [TestMethod]
    public void TheRangeLineGivesRanksOutOfTheBoard()
        => Assert.AreEqual("Ranks 51 to 100 of 8,423", RankingView.RangeText(50, 51, 100, 2, 50, 8423, null));

    [TestMethod]
    public void TheRangeLineOfAFilterCountsMatches()
    {
        Assert.AreEqual("7 players match \"rustic\", showing 1 to 7", RankingView.RangeText(7, 2, 43, 1, 50, 7, " rustic "));
        Assert.AreEqual("1 player matches \"x\", showing 1 to 1", RankingView.RangeText(1, 9, 9, 1, 50, 1, "x"));
        Assert.AreEqual("0 players match \"zz\"", RankingView.RangeText(0, 0, 0, 1, 50, 0, "zz"));
    }

    [TestMethod]
    public void AnEmptyPageHasNoRanks()
        => Assert.AreEqual("No ranks to show", RankingView.RangeText(0, 0, 0, 999, 50, null, null));

    private static readonly LeagueDivision Bronze = new(1, 1, "Bronze");

    private static readonly League[] Leagues = [new(1, "1v1_league", "a"), new(2, "2v2_league", "b")];

    private static readonly LeagueSeason[] Seasons = [new(10, 1, "1v1_league", 1, "s", null, null), new(11, 1, "1v1_league", 2, "s", null, null)];

    private static readonly LeagueSubdivision[] Subdivisions = [new(100, 1, "I", Bronze), new(101, 2, "II", Bronze)];

    [TestMethod]
    public void LeagueParametersThatNameNothingAreStale()
    {
        IReadOnlyList<string> stale = LeagueView.StaleParameters(Leagues, Seasons, Leagues[0], Subdivisions, "zzz", "99999", "5", "abc");

        CollectionAssert.AreEqual(new[] { "league", "season", "subdivision", "page" }, stale.ToArray());
    }

    [TestMethod]
    public void LeagueParametersThatExistAreKept()
        => Assert.AreEqual(0, LeagueView.StaleParameters(Leagues, Seasons, Leagues[0], Subdivisions, "2v2_league", "11", "101", "2").Count);

    [TestMethod]
    public void ASeasonOfAnotherLeagueIsStale()
        => CollectionAssert.AreEqual(new[] { "season" }, LeagueView.StaleParameters(Leagues, Seasons, Leagues[1], Subdivisions, null, "10", null, null).ToArray());

    [TestMethod]
    public void TheStandingsTotalReplacesTheSeparateCount()
    {
        IReadOnlyDictionary<int, int> counts = new Dictionary<int, int> { [100] = 536, [101] = 20 };

        IReadOnlyDictionary<int, int> merged = LeagueView.CountsWithStandings(counts, 100, 412);

        Assert.AreEqual(412, merged[100]);
        Assert.AreEqual(20, merged[101]);
        Assert.AreEqual(536, counts[100]);
        Assert.AreSame(counts, LeagueView.CountsWithStandings(counts, 100, null));
    }

    [TestMethod]
    public void LabelsOverlapOnlyWhenTheirBoxesMeet()
    {
        LabelBox a = new(100, 10, 50, 12);

        Assert.IsTrue(LabelLayout.Overlap(a, new LabelBox(140, 14, 50, 12)));
        Assert.IsFalse(LabelLayout.Overlap(a, new LabelBox(160, 10, 50, 12)));
        Assert.IsFalse(LabelLayout.Overlap(a, new LabelBox(100, 40, 50, 12)));
        Assert.AreEqual(35.0, LabelLayout.Width("Peak ", 7), 0.001);
    }
}
