using System.Text.RegularExpressions;
using FAForever.FileFormats.Blueprints;
using FAForever.Vault.Viewer.Services.Api;
using FAForever.Vault.Viewer.Services.Players;

namespace FAForever.Vault.Viewer.Tests.Players;

/// <summary>The mappers on small hand-written documents: the shapes the fixtures do not hold.</summary>
[TestClass]
public class PlayerMappersTest
{
    private static JsonApiDocument Parse(string json) => JsonApiDocument.Parse(json);

    [TestMethod]
    public void FactionIsReadAsAnIndexOrAName()
    {
        JsonApiDocument document = Parse("""
            {"data":[
              {"type":"gamePlayerStats","id":"1","attributes":{"faction":3,"result":"VICTORY","team":"2"},"relationships":{"game":{"data":{"type":"game","id":"10"}}}},
              {"type":"gamePlayerStats","id":"2","attributes":{"faction":"AEON","result":"DEFEAT"},"relationships":{"game":{"data":{"type":"game","id":"11"}}}},
              {"type":"gamePlayerStats","id":"3","attributes":{"faction":5,"result":"MUTUAL_DRAW"},"relationships":{"game":{"data":{"type":"game","id":"12"}}}},
              {"type":"gamePlayerStats","id":"4","attributes":{"result":"CONFLICTING"}}
            ]}
            """);

        IReadOnlyList<PlayerMatch> matches = PlayerMappers.MapMatches(document, 1);

        Assert.AreEqual(Faction.Cybran, matches[0].Faction);
        Assert.AreEqual(2, matches[0].Team, "Elide sends the team as a string");
        Assert.AreEqual(MatchResult.Victory, matches[0].Result);
        Assert.AreEqual(Faction.Aeon, matches[1].Faction);
        Assert.AreEqual(MatchResult.Defeat, matches[1].Result);
        Assert.IsNull(matches[2].Faction, "random is not a faction");
        Assert.AreEqual(MatchResult.Draw, matches[2].Result);
        Assert.IsNull(matches[3].Faction);
        Assert.AreEqual(MatchResult.Unknown, matches[3].Result);
        Assert.AreEqual(10, matches[0].GameId);
    }

    [TestMethod]
    public void AMatchWithoutIncludesStillMaps()
    {
        IReadOnlyList<PlayerMatch> matches = PlayerMappers.MapMatches(Parse("""{"data":[{"type":"gamePlayerStats","id":"5","attributes":{}}]}"""), 1);

        Assert.AreEqual(1, matches.Count);
        Assert.IsNull(matches[0].MapName);
        Assert.IsNull(matches[0].Duration);
        Assert.IsNull(matches[0].RatingChange);
        Assert.AreEqual(0, matches[0].Players.Count);
    }

    [TestMethod]
    public void TheMatchRatingFallsBackToTheRatingChange()
    {
        JsonApiDocument document = Parse("""
            {"data":[{"type":"gamePlayerStats","id":"1","attributes":{"result":"VICTORY"},
                "relationships":{"ratingChanges":{"data":[{"type":"leaderboardRatingJournal","id":"7"}]}}}],
             "included":[
               {"type":"leaderboardRatingJournal","id":"7","attributes":{"meanBefore":1500,"deviationBefore":100,"meanAfter":1520,"deviationAfter":100},
                "relationships":{"leaderboard":{"data":{"type":"leaderboard","id":"2"}}}},
               {"type":"leaderboard","id":"2","attributes":{"technicalName":"ladder_1v1"}}]}
            """);

        PlayerMatch match = PlayerMappers.MapMatches(document, 1).Single();

        Assert.AreEqual(1200, match.RatingBefore);
        Assert.AreEqual(1220, match.RatingAfter);
        Assert.AreEqual(20, match.RatingChange);
        Assert.AreEqual("ladder_1v1", match.Board);
    }

    [TestMethod]
    public void DurationComesFromTicksOrFromTheClock()
    {
        JsonApiDocument document = Parse("""
            {"data":[
              {"type":"gamePlayerStats","id":"1","relationships":{"game":{"data":{"type":"game","id":"1"}}}},
              {"type":"gamePlayerStats","id":"2","relationships":{"game":{"data":{"type":"game","id":"2"}}}}],
             "included":[
              {"type":"game","id":"1","attributes":{"replayTicks":18000,"startTime":"2026-01-01T10:00:00Z","endTime":"2026-01-01T10:45:00Z"}},
              {"type":"game","id":"2","attributes":{"startTime":"2026-01-01T10:00:00Z","endTime":"2026-01-01T10:20:00Z"}}]}
            """);

        IReadOnlyList<PlayerMatch> matches = PlayerMappers.MapMatches(document, 1);

        Assert.AreEqual(TimeSpan.FromMinutes(30), matches[0].Duration, "18,000 ticks are 30 minutes");
        Assert.AreEqual(TimeSpan.FromMinutes(20), matches[1].Duration);
    }

    [TestMethod]
    public void ProfileLeavesTheCurrentNameOutOfTheFormerNames()
    {
        JsonApiDocument document = Parse("""
            {"data":{"type":"player","id":"3","attributes":{"login":"Now"},
              "relationships":{"names":{"data":[{"type":"nameRecord","id":"1"},{"type":"nameRecord","id":"2"},{"type":"nameRecord","id":"3"}]}}},
             "included":[
              {"type":"nameRecord","id":"1","attributes":{"name":"Old","changeTime":"2022-01-01T00:00:00Z"}},
              {"type":"nameRecord","id":"2","attributes":{"name":"Older","changeTime":"2021-01-01T00:00:00Z"}},
              {"type":"nameRecord","id":"3","attributes":{"name":"Now","changeTime":"2023-01-01T00:00:00Z"}}]}
            """);

        PlayerProfile profile = PlayerMappers.MapProfile(document)!;

        CollectionAssert.AreEqual(new[] { "Old", "Older" }, profile.FormerNames.Select(name => name.Name).ToArray());
        Assert.IsNull(profile.Clan);
        Assert.IsNull(profile.AvatarUrl);
        Assert.IsNull(PlayerMappers.MapProfile(Parse("""{"data":[]}""")));
    }

    [TestMethod]
    public void OpponentsAreTheOtherTeamsOrEverybodyWithoutTeams()
    {
        string Stats(int id, int team)
            => "{\"type\":\"gamePlayerStats\",\"id\":\"" + id + "\",\"attributes\":{\"team\":" + team + "},"
                + "\"relationships\":{\"game\":{\"data\":{\"type\":\"game\",\"id\":\"9\"}},\"player\":{\"data\":{\"type\":\"player\",\"id\":\"" + id + "\"}}}}";
        string Player(int id) => "{\"type\":\"player\",\"id\":\"" + id + "\",\"attributes\":{\"login\":\"P" + id + "\"}}";
        string StatsRef(int id) => "{\"type\":\"gamePlayerStats\",\"id\":\"" + id + "\"}";

        JsonApiDocument Journal(params int[] teams)
        {
            List<string> included = ["{\"type\":\"game\",\"id\":\"9\",\"relationships\":{\"playerStats\":{\"data\":[" + string.Join(",", Enumerable.Range(1, 4).Select(StatsRef)) + "]}}}"];
            included.AddRange(Enumerable.Range(1, 4).Select(id => Stats(id, teams[id - 1])));
            included.AddRange(Enumerable.Range(1, 4).Select(Player));
            return Parse("{\"data\":[{\"type\":\"leaderboardRatingJournal\",\"id\":\"1\",\"attributes\":{\"meanBefore\":1,\"deviationBefore\":0,\"meanAfter\":2,\"deviationAfter\":0},"
                + "\"relationships\":{\"gamePlayerStats\":{\"data\":" + StatsRef(1) + "}}}],\"included\":[" + string.Join(",", included) + "]}");
        }

        IReadOnlyList<RatingJournalEntry> teamed = PlayerMappers.MapJournal(Journal(2, 2, 3, 3), 1);
        CollectionAssert.AreEqual(new[] { 3, 4 }, teamed[0].Opponents.Select(opponent => opponent.Id).ToArray());

        IReadOnlyList<RatingJournalEntry> free = PlayerMappers.MapJournal(Journal(1, 1, 1, 1), 1);
        CollectionAssert.AreEqual(new[] { 2, 3, 4 }, free[0].Opponents.Select(opponent => opponent.Id).ToArray());
    }

    [TestMethod]
    public void AJournalWithoutIncludesStillMaps()
    {
        IReadOnlyList<RatingJournalEntry> entries = PlayerMappers.MapJournal(
            Parse("""{"data":[{"type":"leaderboardRatingJournal","id":"1","attributes":{"meanBefore":1500,"deviationBefore":50,"meanAfter":1510,"deviationAfter":49,"createTime":"2026-02-03T04:05:06Z"}}]}"""), 1);

        Assert.AreEqual(1, entries.Count);
        Assert.AreEqual(new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero), entries[0].Date, "the journal's own time is the fallback");
        Assert.AreEqual(13.0, entries[0].Change, 1e-9);
        Assert.IsNull(entries[0].MapName);
        Assert.AreEqual(0, entries[0].Opponents.Count);
    }

    [TestMethod]
    public void StandingsKeepAnUnresolvedPlayerAsItsId()
    {
        JsonApiDocument scores = Parse("""
            {"data":[{"type":"leagueSeasonScore","id":"1","attributes":{"loginId":42,"score":5,"gameCount":9}},
                     {"type":"leagueSeasonScore","id":"2","attributes":{"loginId":"43","score":4,"gameCount":8}}],
             "meta":{"page":{"totalRecords":120,"totalPages":3}}}
            """);
        JsonApiDocument players = Parse("""{"data":[{"type":"player","id":"43","attributes":{"login":"Known"}}]}""");

        PlayerPage<LeagueStanding> page = PlayerMappers.MapStandings(scores, players, 2, 50);

        Assert.AreEqual(51, page.Items[0].Rank);
        Assert.AreEqual("Player 42", page.Items[0].Login);
        Assert.AreEqual("Known", page.Items[1].Login);
        Assert.AreEqual(3, page.TotalPages);
        CollectionAssert.AreEqual(new[] { 42, 43 }, PlayerMappers.StandingPlayerIds(scores).ToArray());
    }

    [TestMethod]
    public void EventIdsAreStoredInLowerCase()
    {
        PlayerEventCounts counts = PlayerMappers.MapEventCounts(Parse("""
            {"data":[{"type":"playerEvent","id":"1","attributes":{"currentCount":12},"relationships":{"event":{"data":{"type":"event","id":"ABC-DEF"}}}},
                     {"type":"playerEvent","id":"2","attributes":{"currentCount":3}}]}
            """));

        Assert.AreEqual(1, counts.Counts.Count);
        Assert.AreEqual(12, counts.Get("abc-def"));
    }

    [TestMethod]
    public void CountReadsTheTotal()
    {
        Assert.AreEqual(8423, PlayerMappers.MapCount(Parse("""{"data":[],"meta":{"page":{"totalRecords":8423}}}""")));
        Assert.AreEqual(0, PlayerMappers.MapCount(Parse("""{"data":[]}""")));
    }

    [TestMethod]
    public void SeasonsComeLatestFirst()
    {
        IReadOnlyList<LeagueSeason> seasons = PlayerMappers.MapSeasons(Parse("""
            {"data":[{"type":"leagueSeason","id":"1","attributes":{"seasonNumber":1,"startDate":"2025-01-01T00:00:00Z","endDate":"2025-06-01T00:00:00Z"}},
                     {"type":"leagueSeason","id":"2","attributes":{"seasonNumber":2,"startDate":"2025-06-01T00:00:00Z","endDate":"2026-06-01T00:00:00Z"}}]}
            """));

        CollectionAssert.AreEqual(new[] { 2, 1 }, seasons.Select(season => season.Id).ToArray());
        Assert.IsTrue(seasons[0].IsActive(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        Assert.IsFalse(seasons[1].IsActive(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
    }

    [TestMethod]
    public void ResultOf()
    {
        Assert.AreEqual(MatchResult.Victory, PlayerMappers.ResultOf("VICTORY"));
        Assert.AreEqual(MatchResult.Defeat, PlayerMappers.ResultOf("DEFEAT"));
        Assert.AreEqual(MatchResult.Draw, PlayerMappers.ResultOf("DRAW"));
        Assert.AreEqual(MatchResult.Draw, PlayerMappers.ResultOf("MUTUAL_DRAW"));
        Assert.AreEqual(MatchResult.Unknown, PlayerMappers.ResultOf("UNKNOWN"));
        Assert.AreEqual(MatchResult.Unknown, PlayerMappers.ResultOf(null));
    }
}

/// <summary>The requests: what is asked of the API, decoded so the filters read as RSQL.</summary>
[TestClass]
public class PlayerRequestsTest
{
    private static readonly DateTimeOffset Since = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private static string FilterOf(string request)
        => Uri.UnescapeDataString(Regex.Match(request, "filter=([^&]*)").Groups[1].Value);

    [TestMethod]
    public void MatchesFilterEverything()
    {
        string request = PlayerRequests.Matches(2049311, new MatchQuery
        {
            Since = Since,
            Mode = "ladder_1v1",
            Faction = Faction.Cybran,
            Result = MatchResult.Victory,
            Map = "dual",
            WithPlayer = "Some \"One\"",
            Sort = MatchSort.Longest,
            Page = 3,
        });

        Assert.AreEqual(
            "player.id==2049311;scoreTime=ge=2026-01-02T03:04:05Z;ratingChanges.leaderboard.technicalName==\"ladder_1v1\";faction==CYBRAN;result==VICTORY;"
            + "game.mapVersion.map.displayName==\"*dual*\";game.playerStats.player.login==\"Some \\\"One\\\"\"",
            FilterOf(request));
        StringAssert.StartsWith(request, "/data/gamePlayerStats?include=game,game.mapVersion.map,game.featuredMod,game.playerStats,game.playerStats.player,ratingChanges.leaderboard");
        StringAssert.Contains(request, "sort=-game.replayTicks");
        StringAssert.Contains(request, "page[size]=25&page[number]=3&page[totals]");
    }

    [TestMethod]
    public void MatchesWithoutFiltersOnlyFilterThePlayer()
    {
        string request = PlayerRequests.Matches(7, new MatchQuery());

        Assert.AreEqual("player.id==7", FilterOf(request));
        StringAssert.Contains(request, "sort=-scoreTime");
    }

    [TestMethod]
    public void TheChartListIsLightAndBig()
    {
        string request = PlayerRequests.MatchesForCharts(7, new MatchQuery { Mode = "custom" }, 2);

        StringAssert.Contains(request, "page[size]=100&page[number]=2");
        Assert.IsFalse(request.Contains("playerStats"));
        Assert.AreEqual("player.id==7;ratingChanges.leaderboard.technicalName==\"global\"", FilterOf(request), "custom games are the global board's");
    }

    [TestMethod]
    public void ModesAreBoards()
    {
        Assert.IsNull(PlayerRequests.BoardOfMode("all"));
        Assert.IsNull(PlayerRequests.BoardOfMode(null));
        Assert.AreEqual("global", PlayerRequests.BoardOfMode("custom"));
        Assert.AreEqual("tmm_2v2", PlayerRequests.BoardOfMode("tmm_2v2"));
    }

    [TestMethod]
    public void RatingJournalFiltersTheBoardAndTheStart()
    {
        string request = PlayerRequests.RatingJournal(5, "tmm_2v2", Since, 2);

        Assert.AreEqual("gamePlayerStats.player.id==5;leaderboard.technicalName==\"tmm_2v2\";gamePlayerStats.scoreTime=ge=2026-01-02T03:04:05Z", FilterOf(request));
        StringAssert.Contains(request, "page[size]=100&page[number]=2");
        StringAssert.Contains(request, "sort=-gamePlayerStats.scoreTime");
        Assert.AreEqual("player.id==5", FilterOf(PlayerRequests.Ratings(5)));
        Assert.IsFalse(FilterOf(PlayerRequests.RatingJournal(5, "x", null, 1)).Contains("scoreTime"));
    }

    [TestMethod]
    public void RankingFilters()
    {
        RankingQuery query = new() { Board = "ladder_1v1", ActiveSince = Since, Name = "ab", Page = 2 };

        string page = PlayerRequests.Ranking(query);
        Assert.AreEqual("leaderboard.technicalName==\"ladder_1v1\";totalGames=gt=0;deviation=lt=100;updateTime=ge=2026-01-02T03:04:05Z;player.login==\"*ab*\"", FilterOf(page));
        StringAssert.Contains(page, "sort=-rating");
        StringAssert.Contains(page, "page[size]=50&page[number]=2&page[totals]");

        Assert.AreEqual("leaderboard.technicalName==\"ladder_1v1\";totalGames=gt=0;updateTime=ge=2026-01-02T03:04:05Z;player.login==\"*ab*\"",
            FilterOf(PlayerRequests.Ranking(query with { SettledOnly = false })));

        string better = PlayerRequests.BetterRatings(query, 1500);
        Assert.AreEqual("leaderboard.technicalName==\"ladder_1v1\";totalGames=gt=0;deviation=lt=100;updateTime=ge=2026-01-02T03:04:05Z;rating=gt=1500", FilterOf(better), "the name filter does not narrow a rank");
        StringAssert.Contains(better, "page[size]=1&page[totals]");

        StringAssert.EndsWith(FilterOf(PlayerRequests.RatingsBetween(query, 1200, 1300)), ";rating=ge=1200;rating=lt=1300");
    }

    [TestMethod]
    public void SearchUsesWildcards()
    {
        Assert.AreEqual("login==\"*ash*\"", FilterOf(PlayerRequests.FindPlayers(" ash ")));
        Assert.AreEqual("name==\"*ash*\"", FilterOf(PlayerRequests.FindNames("ash")));
        Assert.AreEqual("login==\"Ash\"", FilterOf(PlayerRequests.PlayerByLogin("Ash")));
        StringAssert.Contains(PlayerRequests.FindPlayers("ash"), "page[size]=10");
    }

    [TestMethod]
    public void LeagueRequests()
    {
        Assert.AreEqual("leagueSeasonDivision.leagueSeason.id==9", FilterOf(PlayerRequests.Subdivisions(9)));
        Assert.AreEqual("loginId==5;leagueSeason.id==9", FilterOf(PlayerRequests.LeagueEntry(5, 9)));
        Assert.AreEqual("loginId==5;leagueSeason.endDate=ge=2026-01-02T03:04:05Z", FilterOf(PlayerRequests.ActiveLeagueEntries(5, Since)));
        Assert.AreEqual("leagueSeason.id==9;leagueSeasonDivisionSubdivision.id==3025", FilterOf(PlayerRequests.PlayersInSubdivision(9, 3025)));
        string standings = PlayerRequests.Standings(9, 3025, 2, 50);
        StringAssert.Contains(standings, "sort=-score,-gameCount");
        StringAssert.Contains(standings, "page[number]=2");
        Assert.AreEqual("id=in=(1,2,3)", FilterOf(PlayerRequests.PlayersById([1, 2, 3])));
        StringAssert.Contains(PlayerRequests.LeagueJourney(5, 9, 1), "sort=gameId");
        Assert.AreEqual("enabled==true", FilterOf(PlayerRequests.Leagues()));
    }

    [TestMethod]
    public void HeadToHeadGoesThroughTheStatisticsOfTheFirstPlayer()
    {
        string request = PlayerRequests.HeadToHead(1, 2, Since, 3);

        Assert.AreEqual("player.id==1;game.playerStats.player.id==2;scoreTime=ge=2026-01-02T03:04:05Z", FilterOf(request));
        StringAssert.StartsWith(request, "/data/gamePlayerStats?include=game,game.playerStats.player,game.mapVersion.map,game.featuredMod&sort=-scoreTime");
        StringAssert.Contains(request, "page[size]=100&page[number]=3&page[totals]");
    }

    [TestMethod]
    public void QuoteIsTheClientsQuote()
        => Assert.AreEqual("\"a\\\"b\\\\\"", FafApiClient.Quote("a\"b\\"));
}
