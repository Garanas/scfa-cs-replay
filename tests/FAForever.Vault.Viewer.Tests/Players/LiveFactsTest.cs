using System.Net;
using System.Text.RegularExpressions;
using FAForever.FileFormats.Blueprints;
using FAForever.Vault.Viewer.Services.Api;
using FAForever.Vault.Viewer.Services.Players;
using FAForever.Vault.Viewer.Services.Players.Statistics;

namespace FAForever.Vault.Viewer.Tests.Players;

/// <summary>
/// What the live API answered when the requests were probed (2026-10-16), kept as tests: the profile
/// include, the faction names, the page size and totals, the head-to-head request, a league score that
/// is null, the text of an error, and what the sample data shows.
/// </summary>
[TestClass]
public class LiveFactsTest
{
    private static readonly DateTimeOffset Now = SamplePlayersApi.FixtureAnchor;

    private static SamplePlayersApi Create() => new(new FixedTimeProvider(Now));

    private static CancellationToken Token => CancellationToken.None;

    private static string FilterOf(string request)
        => Uri.UnescapeDataString(Regex.Match(request, "filter=([^&]*)").Groups[1].Value);

    private static JsonApiDocument Parse(string json) => JsonApiDocument.Parse(json);

    // ---------------------------------------------------------------- requests

    [TestMethod]
    public void TheProfileIncludesTheAvatarAssignments()
        => Assert.AreEqual("/data/player/7?include=names,clanMembership.clan,avatarAssignments.avatar", PlayerRequests.Profile(7));

    [TestMethod]
    [DataRow(Faction.Uef, "UEF")]
    [DataRow(Faction.Aeon, "AEON")]
    [DataRow(Faction.Cybran, "CYBRAN")]
    [DataRow(Faction.Seraphim, "SERAPHIM")]
    public void TheFactionFilterTakesTheName(Faction faction, string name)
    {
        string request = PlayerRequests.Matches(5, new MatchQuery { Faction = faction });

        Assert.AreEqual($"player.id==5;faction=={name}", FilterOf(request));
    }

    [TestMethod]
    public void EveryLoopAsksForPagesOfOneHundredWithTheTotals()
    {
        Assert.AreEqual(100, PlayerRequests.LoopPageSize);
        string[] requests =
        [
            PlayerRequests.RatingJournal(1, "global", null, 2),
            PlayerRequests.MatchesForCharts(1, new MatchQuery(), 2),
            PlayerRequests.EventCounts(1, 2),
            PlayerRequests.LeagueJourney(1, 9, 2),
            PlayerRequests.HeadToHead(1, 2, null, 2),
        ];

        foreach (string request in requests)
        {
            StringAssert.Contains(request, "page[size]=100&page[number]=2&page[totals]", request);
        }
    }

    [TestMethod]
    public void APageIsNeverLargerThanTheApiAllows()
    {
        StringAssert.Contains(PlayerRequests.Matches(1, new MatchQuery { PageSize = 1000 }), "page[size]=100&");
        StringAssert.Contains(PlayerRequests.Ranking(new RankingQuery { PageSize = 150 }), "page[size]=100&");
        StringAssert.Contains(PlayerRequests.Matches(1, new MatchQuery { PageSize = 25 }), "page[size]=25&");
    }

    [TestMethod]
    public void MatchesFilterByBoardAndByPlayerLogin()
    {
        Assert.AreEqual(
            "player.id==1;ratingChanges.leaderboard.technicalName==\"global\";game.playerStats.player.login==\"x\"",
            FilterOf(PlayerRequests.Matches(1, new MatchQuery { Mode = "custom", WithPlayer = "x" })),
            "custom games are the global board's");
    }

    [TestMethod]
    public void HeadToHeadIsAskedOfThePlayersStatistics()
    {
        string request = PlayerRequests.HeadToHead(2049311, 100507, null, 1);

        StringAssert.StartsWith(request, "/data/gamePlayerStats?include=game,game.playerStats.player,game.mapVersion.map,game.featuredMod&sort=-scoreTime");
        Assert.AreEqual("player.id==2049311;game.playerStats.player.id==100507", FilterOf(request));
    }

    // ---------------------------------------------------------------- paging

    [TestMethod]
    public void ALoopStopsByThePageTotals()
    {
        JsonApiDocument full = Parse(Rows(100) + ",\"meta\":{\"page\":{\"totalRecords\":218,\"totalPages\":3}}}");
        JsonApiDocument last = Parse(Rows(18) + ",\"meta\":{\"page\":{\"totalRecords\":218,\"totalPages\":3}}}");

        Assert.IsTrue(FafPlayersApi.HasMorePages(full, 1));
        Assert.IsTrue(FafPlayersApi.HasMorePages(full, 2));
        Assert.IsFalse(FafPlayersApi.HasMorePages(full, 3), "a full last page is the last page");
        Assert.IsFalse(FafPlayersApi.HasMorePages(last, 3));
        Assert.IsTrue(FafPlayersApi.HasMorePages(Parse(Rows(18) + ",\"meta\":{\"page\":{\"totalRecords\":218,\"totalPages\":3}}}"), 1), "a short page in the middle does not stop it");
    }

    [TestMethod]
    public void WithoutTotalsAFullPageContinuesAndAnEmptyOneStops()
    {
        Assert.IsTrue(FafPlayersApi.HasMorePages(Parse(Rows(100) + "}"), 1));
        Assert.IsFalse(FafPlayersApi.HasMorePages(Parse(Rows(99) + "}"), 1));
        Assert.IsFalse(FafPlayersApi.HasMorePages(Parse(Rows(0) + ",\"meta\":{\"page\":{\"totalRecords\":0,\"totalPages\":1}}}"), 1));
    }

    private static string Rows(int count)
        => "{\"data\":[" + string.Join(",", Enumerable.Range(1, count).Select(index => $"{{\"type\":\"x\",\"id\":\"{index}\"}}")) + "]";

    // ---------------------------------------------------------------- errors

    [TestMethod]
    public void AnErrorSaysTheStatusAndTheFirstDetail()
    {
        string message = FafApiClient.ErrorMessage(
            HttpStatusCode.BadRequest, "Bad Request",
            """{"errors":[{"detail":"Invalid value: player does not contain the field currentAvatar"},{"detail":"second"}]}""");

        Assert.AreEqual("The FAForever API answered 400 Bad Request: Invalid value: player does not contain the field currentAvatar", message);
    }

    [TestMethod]
    public void AnErrorWithoutAReadableBodyIsTheStatusAlone()
    {
        Assert.AreEqual("The FAForever API answered 500 Internal Server Error", FafApiClient.ErrorMessage(HttpStatusCode.InternalServerError, null, "<html>oops</html>"));
        Assert.AreEqual("The FAForever API answered 404 Not Found", FafApiClient.ErrorMessage(HttpStatusCode.NotFound, "Not Found", """{"errors":[]}"""));
        Assert.AreEqual("The FAForever API answered 400 Bad Request", FafApiClient.ErrorMessage(HttpStatusCode.BadRequest, "", null));
    }

    [TestMethod]
    public void TooManyRequestsAsksToWait()
        => Assert.AreEqual("The FAForever API asks to wait a moment", FafApiClient.ErrorMessage(HttpStatusCode.TooManyRequests, "Too Many Requests", "{}"));

    // ---------------------------------------------------------------- mapping

    [TestMethod]
    public void TheSelectedAssignmentIsTheCurrentAvatar()
    {
        JsonApiDocument document = Parse("""
            {"data":{"type":"player","id":"1","attributes":{"login":"Ash"},"relationships":{"avatarAssignments":{"data":[{"type":"avatarAssignment","id":"10"},{"type":"avatarAssignment","id":"11"}]}}},
             "included":[
               {"type":"avatarAssignment","id":"10","attributes":{"selected":false},"relationships":{"avatar":{"data":{"type":"avatar","id":"1"}}}},
               {"type":"avatarAssignment","id":"11","attributes":{"selected":true,"expiresAt":null},"relationships":{"avatar":{"data":{"type":"avatar","id":"2"}}}},
               {"type":"avatar","id":"1","attributes":{"url":"https://x/old.png","tooltip":"Old"}},
               {"type":"avatar","id":"2","attributes":{"url":"https://x/new.png","tooltip":"New","filename":"new.png"}}]}
            """);

        PlayerProfile? profile = PlayerMappers.MapProfile(document);

        Assert.AreEqual("https://x/new.png", profile!.AvatarUrl);
        Assert.AreEqual("New", profile.AvatarTooltip);
    }

    [TestMethod]
    public void WithNoSelectedAssignmentThereIsNoAvatar()
    {
        JsonApiDocument document = Parse("""
            {"data":{"type":"player","id":"1","attributes":{"login":"Ash"},"relationships":{"avatarAssignments":{"data":[{"type":"avatarAssignment","id":"10"}]}}},
             "included":[
               {"type":"avatarAssignment","id":"10","attributes":{"selected":false},"relationships":{"avatar":{"data":{"type":"avatar","id":"1"}}}},
               {"type":"avatar","id":"1","attributes":{"url":"https://x/old.png"}}]}
            """);

        Assert.IsNull(PlayerMappers.MapProfile(document)!.AvatarUrl);
        Assert.IsNull(PlayerMappers.MapProfile(Parse("""{"data":{"type":"player","id":"1","attributes":{"login":"Ash"}}}"""))!.AvatarUrl);
    }

    [TestMethod]
    public void APlayerInPlacementHasNoScoreAndNoSubdivision()
    {
        IReadOnlyList<LeagueEntry> entries = PlayerMappers.MapLeagueEntries(Parse("""
            {"data":[
              {"type":"leagueSeasonScore","id":"1","attributes":{"loginId":5,"score":null,"gameCount":1,"returningPlayer":false},"relationships":{"leagueSeason":{"data":{"type":"leagueSeason","id":"12"}}}},
              {"type":"leagueSeasonScore","id":"2","attributes":{"loginId":5,"score":0,"gameCount":14,"returningPlayer":true},"relationships":{"leagueSeason":{"data":{"type":"leagueSeason","id":"9"}}}}]}
            """));

        Assert.IsTrue(entries[0].InPlacement);
        Assert.AreEqual(0, entries[0].Score);
        Assert.AreEqual(1, entries[0].GameCount);
        Assert.IsNull(entries[0].Subdivision);
        Assert.IsFalse(entries[1].InPlacement, "a score of 0 is a score");
    }

    [TestMethod]
    public void UnratedGamesKeepNullRatings()
    {
        IReadOnlyList<PlayerMatch> matches = PlayerMappers.MapMatches(Parse("""
            {"data":[{"type":"gamePlayerStats","id":"1","attributes":{"result":"VICTORY","faction":2,"afterMean":null,"afterDeviation":null,"beforeMean":null,"beforeDeviation":null}}]}
            """), 1);

        Assert.IsNull(matches[0].RatingBefore);
        Assert.IsNull(matches[0].RatingAfter);
        Assert.IsNull(matches[0].RatingChange);
        Assert.AreEqual(Faction.Aeon, matches[0].Faction, "the attribute is a number, the filter takes a name");
    }

    [TestMethod]
    public void HeadToHeadReadsTheStatisticsOfTheFirstPlayer()
    {
        JsonApiDocument document = Parse("""
            {"data":[{"type":"gamePlayerStats","id":"1","attributes":{"result":"VICTORY"},"relationships":{"player":{"data":{"type":"player","id":"1"}},"game":{"data":{"type":"game","id":"50"}}}}],
             "included":[
               {"type":"game","id":"50","attributes":{"name":"Duel","startTime":"2026-03-01T10:00:00Z","replayTicks":6000},
                "relationships":{"playerStats":{"data":[{"type":"gamePlayerStats","id":"1"},{"type":"gamePlayerStats","id":"2"}]}}},
               {"type":"gamePlayerStats","id":"1","attributes":{"team":2,"faction":1,"result":"VICTORY"},"relationships":{"player":{"data":{"type":"player","id":"1"}}}},
               {"type":"gamePlayerStats","id":"2","attributes":{"team":3,"faction":3,"result":"DEFEAT"},"relationships":{"player":{"data":{"type":"player","id":"2"}}}},
               {"type":"player","id":"1","attributes":{"login":"A"}},{"type":"player","id":"2","attributes":{"login":"B"}}]}
            """);

        IReadOnlyList<HeadToHeadGame> games = PlayerMappers.MapHeadToHead(document, 1, 2);

        Assert.AreEqual(1, games.Count);
        Assert.AreEqual("A", games[0].A.Login);
        Assert.AreEqual("B", games[0].B.Login);
        Assert.AreEqual(TimeSpan.FromMinutes(10), games[0].Duration);
        Assert.AreEqual(0, PlayerMappers.MapHeadToHead(document, 1, 3).Count, "the other player was not in the game");
    }

    // ---------------------------------------------------------------- sample data

    [TestMethod]
    public async Task TheSampleProfileWearsTheSelectedAvatar()
    {
        PlayerProfile? profile = await Create().GetProfileAsync(SamplePlayersApi.SamplePlayerId, Token);

        Assert.AreEqual("https://content.faforever.com/faf/avatars/bronze_commander.png", profile!.AvatarUrl);
        Assert.AreEqual("Veteran commander", profile.AvatarTooltip);
    }

    [TestMethod]
    public async Task TheRivalsHaveProfilesAndANameSearchFindsAFormerName()
    {
        SamplePlayersApi api = Create();

        PlayerProfile? opponent = await api.GetProfileAsync(100507, Token);
        Assert.AreEqual("RusticPylon_NL", opponent!.Login);
        Assert.IsNotNull(opponent.CreatedAt);
        Assert.IsTrue(opponent.FormerNames.Count > 0);
        Assert.IsNotNull(opponent.AvatarUrl);
        Assert.AreEqual("RusticPylon", (await api.GetProfileAsync(100285, Token))!.Login);

        PlayerSearchHit? byName = await api.FindPlayerByNameAsync("rusticpylon_nl", Token);
        Assert.AreEqual(100507, byName!.Id);

        IReadOnlyList<PlayerSearchHit> former = await api.FindPlayersAsync("stomp", Token);
        Assert.AreEqual(SamplePlayersApi.SamplePlayerId, former.Single().Id);
        Assert.AreEqual("grovestomp", former.Single().MatchedFormerName);
        Assert.AreEqual(100507, (await api.FindPlayersAsync("pylonrustic", Token)).Single().Id);
    }

    [TestMethod]
    public async Task TheLifetimeGamesAreOneNumberEverywhere()
    {
        IPlayersApi api = Create();
        PlayerEventCounts counts = await api.GetEventCountsAsync(SamplePlayersApi.SamplePlayerId, Token);

        long custom = counts.Counts[PlayerEvents.CustomGamesPlayed];
        long ladder = counts.Counts[PlayerEvents.LadderGamesPlayed];
        long byFaction = new[] { PlayerEvents.UefPlays, PlayerEvents.AeonPlays, PlayerEvents.CybranPlays, PlayerEvents.SeraphimPlays }.Sum(id => counts.Counts[id]);

        Assert.AreEqual(3750, custom + ladder, "the overview and the compare count custom and ladder games");
        Assert.AreEqual(custom + ladder, byFaction, "the army tab counts the plays of the four factions");
    }

    [TestMethod]
    public async Task EveryBoardPageAndFilterShowsOtherRows()
    {
        SamplePlayersApi api = Create();
        string[] boards = ["global", "ladder_1v1", "tmm_2v2", "tmm_4v4_full_share", "tmm_4v4_share_until_death", "tmm_3v3"];

        List<string> firstRows = [];
        foreach (string board in boards)
        {
            PlayerPage<RankingRow> page = await api.GetRankingAsync(new RankingQuery { Board = board }, Token);
            Assert.AreEqual(50, page.Items.Count, board);
            firstRows.Add(page.Items[0].Login);
        }
        Assert.AreEqual(boards.Length, firstRows.Distinct().Count(), "every board has other names");

        PlayerPage<RankingRow> one = await api.GetRankingAsync(new RankingQuery { Page = 1 }, Token);
        PlayerPage<RankingRow> two = await api.GetRankingAsync(new RankingQuery { Page = 2 }, Token);
        Assert.IsFalse(one.Items.Select(row => row.Login).Intersect(two.Items.Select(row => row.Login)).Any(), "every page has other names");
        Assert.AreEqual(51, two.Items[0].Rank);
        Assert.IsTrue(two.Items[0].Rating < one.Items[^1].Rating, "the ratings go down the pages");

        PlayerPage<RankingRow> everybody = await api.GetRankingAsync(new RankingQuery { SettledOnly = false }, Token);
        Assert.IsTrue(everybody.TotalRecords > one.TotalRecords, "the settled filter narrows the list");
        Assert.IsTrue(everybody.Items.Any(row => row.Deviation >= 100), "unsettled ratings appear without it");
        Assert.IsTrue(one.Items.All(row => row.Deviation < 100));

        PlayerPage<RankingRow> lately = await api.GetRankingAsync(new RankingQuery { ActiveSince = Now.AddDays(-7) }, Token);
        Assert.IsTrue(lately.TotalRecords < one.TotalRecords, "the played-in filter narrows the list");
        Assert.IsTrue(lately.Items.All(row => row.UpdatedAt >= Now.AddDays(-7)));
    }

    [TestMethod]
    public async Task EverySubdivisionHasItsOwnStandingsOfItsOwnSize()
    {
        SamplePlayersApi api = Create();
        IReadOnlyList<LeagueSubdivision> layout = await api.GetSubdivisionsAsync(9, Token);
        IReadOnlyDictionary<int, int> counts = await api.GetPlayersPerSubdivisionAsync(9, layout, Token);

        LeagueSubdivision a = layout[0];
        LeagueSubdivision b = layout[10];
        PlayerPage<LeagueStanding> first = await api.GetStandingsAsync(9, a.Id, 1, Token);
        PlayerPage<LeagueStanding> other = await api.GetStandingsAsync(9, b.Id, 1, Token);

        Assert.AreEqual(counts[a.Id], first.TotalRecords);
        Assert.AreEqual(counts[b.Id], other.TotalRecords);
        Assert.AreEqual(Math.Min(50, counts[a.Id]), first.Items.Count);
        Assert.IsFalse(first.Items.Select(row => row.Login).SequenceEqual(other.Items.Select(row => row.Login)));
        Assert.IsTrue(first.Items.All(row => row.PlayerId != SamplePlayersApi.SamplePlayerId));

        PlayerPage<LeagueStanding> second = await api.GetStandingsAsync(9, b.Id, 2, Token);
        Assert.AreEqual(Math.Min(50, Math.Max(0, counts[b.Id] - 50)), second.Items.Count);
        if (second.Items.Count > 0)
        {
            Assert.AreEqual(51, second.Items[0].Rank);
        }
    }

    [TestMethod]
    public async Task TheSamplePlayerIsInPlacementInTheRunningTwoVsTwoSeason()
    {
        SamplePlayersApi api = Create();
        IReadOnlyList<LeagueEntry> active = await api.GetActiveLeagueEntriesAsync(SamplePlayersApi.SamplePlayerId, Token);

        LeagueEntry placement = active.Single(entry => entry.InPlacement);
        Assert.AreEqual(12, placement.SeasonId);
        Assert.IsNull(placement.Subdivision);
        Assert.AreEqual(0, placement.Score);
        Assert.IsFalse(active.Single(entry => !entry.InPlacement).InPlacement);
    }
}
