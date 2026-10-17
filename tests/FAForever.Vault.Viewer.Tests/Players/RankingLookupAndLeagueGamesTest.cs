using System.Text.RegularExpressions;
using FAForever.Vault.Viewer.Features.Players.Leagues;
using FAForever.Vault.Viewer.Features.Players.Rankings;
using FAForever.Vault.Viewer.Services.Api;
using FAForever.Vault.Viewer.Services.Players;

namespace FAForever.Vault.Viewer.Tests.Players;

/// <summary>Finding a player in a ranking, and the latest games of a league's players.</summary>
[TestClass]
public class RankingLookupAndLeagueGamesTest
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 34, 56, TimeSpan.Zero);
    private static readonly Leaderboard Ladder = new(2, "ladder_1v1");

    private static string FilterOf(string request)
        => Uri.UnescapeDataString(Regex.Match(request, "filter=([^&]*)").Groups[1].Value);

    private static PlayerRating Rating(double mean, double deviation, DateTimeOffset? updated, int games = 10)
        => new(Ladder, mean, deviation, games, games / 2) { UpdatedAt = updated };

    // ---------------------------------------------------------------- the player in the address

    [TestMethod]
    [DataRow("100285", 100285)]
    [DataRow("0", 0)]
    [DataRow("-4", 0)]
    [DataRow("rust", 0)]
    [DataRow(null, 0)]
    public void ThePlayerOfTheAddressIsAPositiveId(string? token, int expected)
        => Assert.AreEqual(expected, RankingView.PlayerId(token));

    [TestMethod]
    public void AnUnsettledRatingIsOutWhenOnlySettledOnesShow()
    {
        RankingQuery query = RankingView.ToQuery(null, null, "all", null, Now);

        Assert.AreEqual(RankingView.PlayerPlace.FilteredOut, RankingView.Place(Rating(1500, 120, Now), query));
        Assert.AreEqual(RankingView.PlayerPlace.Listed, RankingView.Place(Rating(1500, 120, Now), query with { SettledOnly = false }));
        Assert.AreEqual(RankingView.PlayerPlace.Listed, RankingView.Place(Rating(1500, 99, Now), query));
    }

    [TestMethod]
    public void ARatingNotPlayedInTheChosenPeriodIsOut()
    {
        RankingQuery query = RankingView.ToQuery(null, null, "30d", null, Now);

        Assert.AreEqual(RankingView.PlayerPlace.Listed, RankingView.Place(Rating(1500, 80, Now.AddDays(-5)), query));
        Assert.AreEqual(RankingView.PlayerPlace.FilteredOut, RankingView.Place(Rating(1500, 80, Now.AddDays(-45)), query));
        Assert.AreEqual(RankingView.PlayerPlace.FilteredOut, RankingView.Place(Rating(1500, 80, null), query));
        Assert.AreEqual(RankingView.PlayerPlace.Listed, RankingView.Place(Rating(1500, 80, Now.AddDays(-400)), RankingView.ToQuery(null, null, "all", null, Now)));
    }

    [TestMethod]
    public void NoRatingOnTheBoardIsSaidFirst()
    {
        RankingQuery query = RankingView.ToQuery(null, null, null, null, Now);

        Assert.AreEqual(RankingView.PlayerPlace.NoRating, RankingView.Place(null, query));
        Assert.AreEqual(RankingView.PlayerPlace.NoRating, RankingView.Place(Rating(1500, 80, Now, games: 0), query));
    }

    [TestMethod]
    public void TheMessagesNameThePlayerAndTheBoard()
    {
        RankingQuery settled = new();
        Assert.AreEqual("Ash has no settled rating on Ladder 1v1", RankingView.PlaceMessage(RankingView.PlayerPlace.NoRating, "Ash", settled, "Ladder 1v1"));
        Assert.AreEqual("Ash has no rating on Ladder 1v1", RankingView.PlaceMessage(RankingView.PlayerPlace.NoRating, "Ash", settled with { SettledOnly = false }, "Ladder 1v1"));
        Assert.AreEqual("Ash is not in this ranking with these filters", RankingView.PlaceMessage(RankingView.PlayerPlace.FilteredOut, "Ash", settled, "Ladder 1v1"));
        Assert.IsNull(RankingView.PlaceMessage(RankingView.PlayerPlace.Listed, "Ash", settled, "Ladder 1v1"));
    }

    [TestMethod]
    [DataRow(1, 1)]
    [DataRow(50, 1)]
    [DataRow(51, 2)]
    [DataRow(2049, 41)]
    public void TheFoundPlayerSRankOpensItsPage(int rank, int page)
        => Assert.AreEqual(page, RankingView.PageOfRank(rank));

    // ---------------------------------------------------------------- the request

    [TestMethod]
    public void TheGamesOfPlayersAreAskedByIdsBoardAndSeasonStart()
    {
        string request = PlayerRequests.GamesOfPlayers([11, 22, 33], "ladder_1v1", new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero), 2);

        StringAssert.StartsWith(request, "/data/gamePlayerStats?include=game,game.mapVersion.map,game.playerStats.player&sort=-scoreTime&page[size]=20&page[number]=2&page[totals]&filter=");
        Assert.AreEqual("player.id=in=(11,22,33);scoreTime=ge=2026-08-03T00:00:00Z;ratingChanges.leaderboard.technicalName==\"ladder_1v1\"", FilterOf(request));
    }

    [TestMethod]
    public void NoMoreThanFiftyIdsGoInOneRequest()
    {
        int[] ids = [.. Enumerable.Range(1, 80)];

        string request = PlayerRequests.GamesOfPlayers(ids, "ladder_1v1", Now, 1);

        string list = Regex.Match(FilterOf(request), @"in=\(([^)]*)\)").Groups[1].Value;
        Assert.AreEqual(50, list.Split(',').Length);
        Assert.AreEqual(50, PlayerRequests.MaxIdsPerRequest);
    }

    // ---------------------------------------------------------------- the mapping

    private const string TwoListedPlayersInOneGame = """
        {"data":[
           {"type":"gamePlayerStats","id":"1","attributes":{"result":"VICTORY","team":2,"scoreTime":"2026-10-10T10:20:00Z"},"relationships":{"player":{"data":{"type":"player","id":"11"}},"game":{"data":{"type":"game","id":"500"}}}},
           {"type":"gamePlayerStats","id":"2","attributes":{"result":"DEFEAT","team":3,"scoreTime":"2026-10-10T10:20:00Z"},"relationships":{"player":{"data":{"type":"player","id":"22"}},"game":{"data":{"type":"game","id":"500"}}}},
           {"type":"gamePlayerStats","id":"3","attributes":{"result":"DEFEAT","team":2,"scoreTime":"2026-10-09T10:20:00Z"},"relationships":{"player":{"data":{"type":"player","id":"22"}},"game":{"data":{"type":"game","id":"499"}}}}],
         "included":[
           {"type":"game","id":"500","attributes":{"name":"A","startTime":"2026-10-10T10:00:00Z","replayTicks":6000},"relationships":{"playerStats":{"data":[{"type":"gamePlayerStats","id":"1"},{"type":"gamePlayerStats","id":"2"}]}}},
           {"type":"game","id":"499","attributes":{"name":"B","startTime":"2026-10-09T10:00:00Z","replayTicks":6000},"relationships":{"playerStats":{"data":[{"type":"gamePlayerStats","id":"3"},{"type":"gamePlayerStats","id":"4"}]}}},
           {"type":"gamePlayerStats","id":"1","attributes":{"team":2,"result":"VICTORY"},"relationships":{"player":{"data":{"type":"player","id":"11"}}}},
           {"type":"gamePlayerStats","id":"2","attributes":{"team":3,"result":"DEFEAT"},"relationships":{"player":{"data":{"type":"player","id":"22"}}}},
           {"type":"gamePlayerStats","id":"3","attributes":{"team":2,"result":"DEFEAT"},"relationships":{"player":{"data":{"type":"player","id":"22"}}}},
           {"type":"gamePlayerStats","id":"4","attributes":{"team":3,"result":"VICTORY"},"relationships":{"player":{"data":{"type":"player","id":"99"}}}},
           {"type":"player","id":"11","attributes":{"login":"Eleven"}},{"type":"player","id":"22","attributes":{"login":"TwentyTwo"}},{"type":"player","id":"99","attributes":{"login":"Outsider"}}]}
        """;

    [TestMethod]
    public void AGameOfTwoListedPlayersIsListedOnce()
    {
        IReadOnlyList<PlayerMatch> games = PlayerMappers.MapGamesOfPlayers(JsonApiDocument.Parse(TwoListedPlayersInOneGame));

        CollectionAssert.AreEqual(new[] { 500, 499 }, games.Select(game => game.GameId).ToArray());
        Assert.AreEqual(11, games[0].ListedPlayerId, "the first row stands");
        Assert.AreEqual(MatchResult.Victory, games[0].Result);
        Assert.AreEqual(22, games[1].ListedPlayerId);
        Assert.AreEqual(2, games[0].Players.Count);
    }

    [TestMethod]
    public void TheResultReadsFromTheListedPlayersView()
    {
        IReadOnlyList<PlayerMatch> games = PlayerMappers.MapGamesOfPlayers(JsonApiDocument.Parse(TwoListedPlayersInOneGame));

        Assert.AreEqual("Eleven won", LeagueGames.ResultText(games[0]));
        Assert.AreEqual("TwentyTwo lost", LeagueGames.ResultText(games[1]));
    }

    [TestMethod]
    public void TheSidesAreTheTeamsInOrder()
    {
        IReadOnlyList<PlayerMatch> games = PlayerMappers.MapGamesOfPlayers(JsonApiDocument.Parse(TwoListedPlayersInOneGame));

        IReadOnlyList<IReadOnlyList<MatchParticipant>> sides = LeagueGames.Sides(games[1]);

        Assert.AreEqual(2, sides.Count);
        Assert.AreEqual("TwentyTwo", sides[0].Single().Login);
        Assert.AreEqual("Outsider", sides[1].Single().Login);
    }

    [TestMethod]
    public void ThePageLine()
    {
        Assert.AreEqual("Page 2 of 5", LeagueGames.RangeText(20, 2, 5));
        Assert.AreEqual("Page 1", LeagueGames.RangeText(3, 1, null));
        Assert.AreEqual("No games to show", LeagueGames.RangeText(0, 1, 0));
    }

    // ---------------------------------------------------------------- the sample

    private static async Task<(SamplePlayersApi Api, IReadOnlyList<int> Ids, LeagueSeason Season)> SampleStandings()
    {
        SamplePlayersApi api = new(new FixedTimeProvider(SamplePlayersApi.FixtureAnchor));
        LeagueSeason season = (await api.GetSeasonsAsync(CancellationToken.None)).First(season => season.IsActive(SamplePlayersApi.FixtureAnchor) && season.LeagueTechnicalName == "1v1_league");
        LeagueEntry entry = (await api.GetLeagueEntryAsync(SamplePlayersApi.SamplePlayerId, season.Id, CancellationToken.None))!;
        PlayerPage<LeagueStanding> standings = await api.GetStandingsAsync(season.Id, entry.Subdivision!.Id, 1, CancellationToken.None);
        return (api, [.. standings.Items.Select(row => row.PlayerId)], season);
    }

    [TestMethod]
    public async Task TheSampleHasGamesOfTheStandingsPage()
    {
        (SamplePlayersApi api, IReadOnlyList<int> ids, LeagueSeason season) = await SampleStandings();

        PlayerPage<PlayerMatch> first = await api.GetGamesOfPlayersAsync(ids, season.BoardTechnicalName!, season.StartDate!.Value, 1, CancellationToken.None);
        PlayerPage<PlayerMatch> second = await api.GetGamesOfPlayersAsync(ids, season.BoardTechnicalName!, season.StartDate!.Value, 2, CancellationToken.None);

        Assert.IsTrue(first.Items.Count is > 10 and <= 20, "a page of 20 rows, the games two listed players were in counted once");
        Assert.IsTrue(first.TotalPages >= 2);
        Assert.IsTrue(second.Items.Count > 0);
        PlayerMatch[] all = [.. first.Items, .. second.Items];
        Assert.AreEqual(first.Items.Count, first.Items.Select(game => game.GameId).Distinct().Count(), "a game is listed once on a page");
        Assert.IsTrue(all.All(game => game.ListedPlayerId is { } id && ids.Contains(id) && game.MapName is { Length: > 0 } && game.Duration > TimeSpan.Zero));
        Assert.IsTrue(first.Items.Zip(first.Items.Skip(1)).All(pair => pair.First.StartTime >= pair.Second.StartTime), "newest first");
    }

    [TestMethod]
    public async Task TheSampleHasNoGamesBeforeTheSeasonOrOnOtherBoards()
    {
        (SamplePlayersApi api, IReadOnlyList<int> ids, LeagueSeason season) = await SampleStandings();

        Assert.AreEqual(0, (await api.GetGamesOfPlayersAsync(ids, season.BoardTechnicalName!, SamplePlayersApi.FixtureAnchor.AddDays(1), 1, CancellationToken.None)).Items.Count);
        Assert.AreEqual(0, (await api.GetGamesOfPlayersAsync(ids, "tmm_2v2", season.StartDate!.Value, 1, CancellationToken.None)).Items.Count);
        Assert.AreEqual(0, (await api.GetGamesOfPlayersAsync([1, 2, 3], season.BoardTechnicalName!, season.StartDate!.Value, 1, CancellationToken.None)).Items.Count);
    }

    [TestMethod]
    public async Task TheSampleKeepsOnlyTheAskedPlayersGames()
    {
        (SamplePlayersApi api, IReadOnlyList<int> ids, LeagueSeason season) = await SampleStandings();
        int one = ids[3];

        PlayerPage<PlayerMatch> page = await api.GetGamesOfPlayersAsync([one], season.BoardTechnicalName!, season.StartDate!.Value, 1, CancellationToken.None);

        Assert.IsTrue(page.Items.All(game => game.ListedPlayerId == one && game.Players.Any(player => player.PlayerId == one)));
    }
}
