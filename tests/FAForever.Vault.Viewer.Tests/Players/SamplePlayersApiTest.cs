using FAForever.FileFormats.Blueprints;
using FAForever.Vault.Viewer.Services.Api;
using FAForever.Vault.Viewer.Services.Players;
using FAForever.Vault.Viewer.Services.Players.Statistics;

namespace FAForever.Vault.Viewer.Tests.Players;

/// <summary>
/// The fixtures through the real mappers: what the sample API answers is what the pages get, so the
/// counts and a few exact values pin the fixtures (tools/players-fixtures.py) and the mapping down.
/// </summary>
[TestClass]
public class SamplePlayersApiTest
{
    private const int Rival = 100285;
    private const int OtherRival = 100507;

    private static readonly DateTimeOffset Now = SamplePlayersApi.FixtureAnchor;

    private static SamplePlayersApi Create(DateTimeOffset? now = null) => new(new FixedTimeProvider(now ?? Now));

    private static CancellationToken Token => CancellationToken.None;

    [TestMethod]
    public void EveryFixtureIsEmbeddedAndParses()
    {
        Assert.AreEqual(27, (SamplePlayersApi.ResourceNames).Count);
        foreach (string name in SamplePlayersApi.ResourceNames)
        {
            using Stream stream = typeof(SamplePlayersApi).Assembly.GetManifestResourceStream("players-sample/" + name)!;
            using StreamReader reader = new(stream);
            System.Text.Json.JsonDocument.Parse(reader.ReadToEnd()).Dispose();
        }
    }

    [TestMethod]
    public async Task ProfileHasTheClanAndTwoFormerNames()
    {
        PlayerProfile? profile = await Create().GetProfileAsync(SamplePlayersApi.SamplePlayerId, Token);

        Assert.IsNotNull(profile);
        Assert.AreEqual("Ashgrove", profile.Login);
        Assert.AreEqual("IHV", profile.Clan?.Tag);
        Assert.AreEqual("Iron Harvest", profile.Clan?.Name);
        CollectionAssert.AreEqual(new[] { "Ash_Grove", "grovestomp" }, profile.FormerNames.Select(name => name.Name).ToArray());
        Assert.AreEqual(new DateTimeOffset(2021, 5, 14, 21, 3, 0, TimeSpan.Zero), profile.CreatedAt);
        Assert.IsNotNull(profile.AvatarUrl);
    }

    [TestMethod]
    public async Task AnUnknownPlayerHasNoProfile()
        => Assert.IsNull(await Create().GetProfileAsync(1, Token));

    [TestMethod]
    public async Task SearchFindsCurrentAndFormerNames()
    {
        SamplePlayersApi api = Create();

        IReadOnlyList<PlayerSearchHit> byLogin = await api.FindPlayersAsync("ashgr", Token);
        Assert.AreEqual(1, (byLogin).Count);
        Assert.AreEqual(2049311, byLogin[0].Id);
        Assert.AreEqual("IHV", byLogin[0].ClanTag);
        Assert.IsNull(byLogin[0].MatchedFormerName);

        IReadOnlyList<PlayerSearchHit> byFormerName = await api.FindPlayersAsync("stomp", Token);
        Assert.AreEqual(1, (byFormerName).Count);
        Assert.AreEqual("Ashgrove", byFormerName[0].Login);
        Assert.AreEqual("grovestomp", byFormerName[0].MatchedFormerName);

        Assert.AreEqual(0, (await api.FindPlayersAsync("nobody-has-this-name", Token)).Count);
        Assert.AreEqual(0, (await api.FindPlayersAsync("  ", Token)).Count);
        Assert.AreEqual(2049311, (await api.FindPlayerByNameAsync("ASHGROVE", Token))?.Id);
    }

    [TestMethod]
    public async Task LeaderboardsAndRatings()
    {
        SamplePlayersApi api = Create();

        IReadOnlyList<Leaderboard> boards = await api.GetLeaderboardsAsync(Token);
        CollectionAssert.AreEqual(
            new[] { "Global", "Ladder 1v1", "2v2", "4v4 full share", "4v4 share until death" },
            boards.Select(board => board.DisplayName).ToArray());

        IReadOnlyList<PlayerRating> ratings = await api.GetRatingsAsync(SamplePlayersApi.SamplePlayerId, Token);
        Assert.AreEqual(4, (ratings).Count);
        Assert.AreEqual("ladder_1v1", ratings[0].Board.TechnicalName, "the board with most games comes first");
        PlayerRating ladder = ratings.Single(rating => rating.Board.TechnicalName == "ladder_1v1");
        Assert.AreEqual(1453.66, ladder.Mean, 0.001);
        Assert.AreEqual(46.0, ladder.Deviation, 0.001);
        Assert.AreEqual(1316, ladder.Rating, "1453.66 - 3 x 46, rounded");
        Assert.AreEqual(1910, ladder.TotalGames);
        Assert.AreEqual(1031.0 / 1910, ladder.WinShare!.Value, 1e-9);

        // the rating is the last game's rating after
        IReadOnlyList<RatingJournalEntry> history = await api.GetRatingHistoryAsync(SamplePlayersApi.SamplePlayerId, "ladder_1v1", null, null, Token);
        Assert.AreEqual(ladder.Rating, (int)Math.Round(history[^1].RatingAfter));
    }

    [TestMethod]
    public async Task OtherPlayersGetMovedRatings()
    {
        SamplePlayersApi api = Create();
        PlayerRating mine = (await api.GetRatingsAsync(SamplePlayersApi.SamplePlayerId, Token)).Single(rating => rating.Board.TechnicalName == "ladder_1v1");
        PlayerRating theirs = (await api.GetRatingsAsync(Rival, Token)).Single(rating => rating.Board.TechnicalName == "ladder_1v1");

        Assert.AreNotEqual(mine.Mean, theirs.Mean);
        Assert.IsTrue(Math.Abs(mine.Mean - theirs.Mean) < 200);
    }

    [TestMethod]
    [DataRow("ladder_1v1", 200)]
    [DataRow("global", 70)]
    [DataRow("tmm_2v2", 45)]
    [DataRow("tmm_4v4_full_share", 25)]
    [DataRow("tmm_4v4_share_until_death", 0)]
    public async Task RatingHistoryHasAYearOfGames(string board, int expected)
    {
        int reported = 0;
        IReadOnlyList<RatingJournalEntry> history = await Create().GetRatingHistoryAsync(
            SamplePlayersApi.SamplePlayerId, board, null, new Progress<int>(count => reported = count), Token);

        Assert.AreEqual(expected, (history).Count);
        Assert.IsTrue(history.Zip(history.Skip(1)).All(pair => pair.First.Date <= pair.Second.Date), "oldest first");
        Assert.IsTrue(history.All(entry => entry.MapName is { Length: > 0 } && entry.MapFolder is { Length: > 0 } && entry.Opponents.Count > 0));
        Assert.IsTrue(history.All(entry => entry.Date >= Now.AddDays(-366) && entry.Date <= Now));
        await Task.Delay(1); // Progress<T> posts to the thread pool
        Assert.AreEqual(expected == 0 ? 0 : expected, reported);
    }

    [TestMethod]
    public async Task LadderHistoryHasOneOpponentAndTeamBoardsMore()
    {
        SamplePlayersApi api = Create();
        IReadOnlyList<RatingJournalEntry> ladder = await api.GetRatingHistoryAsync(SamplePlayersApi.SamplePlayerId, "ladder_1v1", null, null, Token);
        IReadOnlyList<RatingJournalEntry> twos = await api.GetRatingHistoryAsync(SamplePlayersApi.SamplePlayerId, "tmm_2v2", null, null, Token);
        IReadOnlyList<RatingJournalEntry> fours = await api.GetRatingHistoryAsync(SamplePlayersApi.SamplePlayerId, "tmm_4v4_full_share", null, null, Token);

        Assert.IsTrue(ladder.All(entry => entry.Opponents.Count == 1));
        Assert.IsTrue(twos.All(entry => entry.Opponents.Count == 2));
        Assert.IsTrue(fours.All(entry => entry.Opponents.Count == 4));
        Assert.IsTrue(ladder.All(entry => entry.Opponents.All(opponent => opponent.Id != SamplePlayersApi.SamplePlayerId)));
    }

    [TestMethod]
    public async Task RatingHistorySinceKeepsTheLaterGames()
    {
        SamplePlayersApi api = Create();
        IReadOnlyList<RatingJournalEntry> all = await api.GetRatingHistoryAsync(SamplePlayersApi.SamplePlayerId, "ladder_1v1", null, null, Token);
        IReadOnlyList<RatingJournalEntry> month = await api.GetRatingHistoryAsync(SamplePlayersApi.SamplePlayerId, "ladder_1v1", Periods.Since("1m", Now), null, Token);

        Assert.IsTrue(month.Count < all.Count);
        Assert.IsTrue((month).Count > 0);
        Assert.IsTrue(month.All(entry => entry.Date >= Now.AddMonths(-1)));
    }

    [TestMethod]
    public async Task TheFixtureDatesFollowTheClockByWholeWeeks()
    {
        IReadOnlyList<RatingJournalEntry> then = await Create().GetRatingHistoryAsync(SamplePlayersApi.SamplePlayerId, "ladder_1v1", null, null, Token);
        IReadOnlyList<RatingJournalEntry> later = await Create(Now.AddDays(25)).GetRatingHistoryAsync(SamplePlayersApi.SamplePlayerId, "ladder_1v1", null, null, Token);

        Assert.AreEqual(then[^1].Date!.Value.AddDays(21), later[^1].Date);
        Assert.AreEqual(then[^1].Date!.Value.DayOfWeek, later[^1].Date!.Value.DayOfWeek);
    }

    [TestMethod]
    public async Task MatchesAreAPageOfTheLatestGames()
    {
        PlayerPage<PlayerMatch> page = await Create().GetMatchesAsync(SamplePlayersApi.SamplePlayerId, new MatchQuery(), Token);

        Assert.AreEqual(25, (page.Items).Count);
        Assert.AreEqual(120, page.TotalRecords);
        Assert.AreEqual(5, page.TotalPages);
        Assert.IsTrue(page.Items.Zip(page.Items.Skip(1)).All(pair => pair.First.StartTime >= pair.Second.StartTime), "newest first");
        Assert.IsTrue(page.Items.All(match => match.Players.Any(player => player.PlayerId == SamplePlayersApi.SamplePlayerId)));
        Assert.IsTrue(page.Items.All(match => match.Players.Count >= 2 && match.MapName is { Length: > 0 } && match.Duration > TimeSpan.Zero));
        Assert.IsTrue(page.Items.All(match => match.Result is MatchResult.Victory or MatchResult.Defeat));

        PlayerMatch newest = page.Items[0];
        Assert.AreEqual(Faction.Seraphim, newest.Faction);
        Assert.AreEqual(MatchResult.Defeat, newest.Result);
        Assert.AreEqual(317, newest.Score);
        Assert.AreEqual("global", newest.Board);
        Assert.AreEqual(1694, newest.RatingBefore, "1831.98 - 3 x 46");
        Assert.AreEqual(1692, newest.RatingAfter, "1829.86 - 3 x 46");
        Assert.AreEqual(-2, newest.RatingChange);
    }

    [TestMethod]
    public async Task MatchFiltersAndPaging()
    {
        SamplePlayersApi api = Create();
        int player = SamplePlayersApi.SamplePlayerId;

        PlayerPage<PlayerMatch> second = await api.GetMatchesAsync(player, new MatchQuery { Page = 2 }, Token);
        Assert.AreEqual(2, second.Page);
        Assert.AreEqual(25, (second.Items).Count);

        PlayerPage<PlayerMatch> ladder = await api.GetMatchesAsync(player, new MatchQuery { Mode = "ladder_1v1", PageSize = 200 }, Token);
        Assert.IsTrue(ladder.Items.Count > 20);
        Assert.IsTrue(ladder.Items.All(match => match.Board == "ladder_1v1" && match.Players.Count == 2));

        PlayerPage<PlayerMatch> custom = await api.GetMatchesAsync(player, new MatchQuery { Mode = "custom", PageSize = 200 }, Token);
        Assert.IsTrue(custom.Items.All(match => match.Board == "global"));

        PlayerPage<PlayerMatch> wins = await api.GetMatchesAsync(player, new MatchQuery { Result = MatchResult.Victory, PageSize = 200 }, Token);
        Assert.IsTrue(wins.Items.All(match => match.Result == MatchResult.Victory));
        Assert.IsTrue(wins.TotalRecords > 0);

        PlayerPage<PlayerMatch> cybran = await api.GetMatchesAsync(player, new MatchQuery { Faction = Faction.Cybran, PageSize = 200 }, Token);
        Assert.IsTrue(cybran.Items.All(match => match.Faction == Faction.Cybran));

        string map = ladder.Items[0].MapName!;
        string word = map.Split(' ')[0];
        PlayerPage<PlayerMatch> onMap = await api.GetMatchesAsync(player, new MatchQuery { Map = word.ToLowerInvariant(), PageSize = 200 }, Token);
        Assert.IsTrue(onMap.Items.All(match => match.MapName!.Contains(word, StringComparison.OrdinalIgnoreCase)), map);

        string opponent = ladder.Items[0].Players.First(participant => participant.PlayerId != player).Login;
        PlayerPage<PlayerMatch> against = await api.GetMatchesAsync(player, new MatchQuery { WithPlayer = opponent, PageSize = 200 }, Token);
        Assert.IsTrue(against.Items.Count > 0);
        Assert.IsTrue(against.Items.All(match => match.Players.Any(participant => participant.Login == opponent)));

        PlayerPage<PlayerMatch> longest = await api.GetMatchesAsync(player, new MatchQuery { Sort = MatchSort.Longest }, Token);
        Assert.IsTrue(longest.Items.Zip(longest.Items.Skip(1)).All(pair => pair.First.Duration >= pair.Second.Duration));

        PlayerPage<PlayerMatch> recent = await api.GetMatchesAsync(player, new MatchQuery { Since = Periods.Since("1m", Now), PageSize = 200 }, Token);
        Assert.IsTrue(recent.Items.All(match => match.StartTime >= Now.AddMonths(-1)));
    }

    [TestMethod]
    public async Task AllMatchesAreTheLightList()
    {
        int reported = 0;
        IReadOnlyList<PlayerMatch> all = await Create().GetAllMatchesAsync(SamplePlayersApi.SamplePlayerId, new MatchQuery(), new Progress<int>(count => reported = count), Token);

        Assert.AreEqual(120, (all).Count);
        Assert.IsTrue(all.All(match => match.Players.Count == 0));
        await Task.Delay(1);
        Assert.AreEqual(120, reported);
    }

    [TestMethod]
    public async Task EventCountsHaveEveryEvent()
    {
        PlayerEventCounts counts = await Create().GetEventCountsAsync(SamplePlayersApi.SamplePlayerId, Token);

        Assert.AreEqual(28, (counts.Counts).Count);
        foreach (string id in PlayerEvents.All)
        {
            Assert.IsTrue(counts.Get(id) > 0, id);
        }
        Assert.AreEqual(1840, counts.Get(PlayerEvents.CustomGamesPlayed));
        Assert.AreEqual(1840, counts.Get(PlayerEvents.CustomGamesPlayed.ToUpperInvariant()), "ids are matched in any case");
        Assert.AreEqual(0, counts.Get("00000000-0000-0000-0000-000000000000"));
    }

    [TestMethod]
    public async Task Achievements()
    {
        SamplePlayersApi api = Create();
        IReadOnlyList<AchievementDefinition> definitions = await api.GetAchievementsAsync(Token);
        IReadOnlyList<PlayerAchievement> mine = await api.GetPlayerAchievementsAsync(SamplePlayersApi.SamplePlayerId, Token);

        Assert.AreEqual(12, (definitions).Count);
        Assert.IsTrue(definitions.Zip(definitions.Skip(1)).All(pair => pair.First.Order < pair.Second.Order));
        Assert.AreEqual(AchievementKind.Incremental, definitions[1].Kind);
        Assert.AreEqual(25, definitions[1].TotalSteps);
        Assert.AreEqual("Commander in Chief", definitions[1].Name);
        Assert.IsTrue(definitions.All(definition => definition.UnlockersPercent is > 0 and <= 100));

        Assert.AreEqual(9, (mine).Count);
        Assert.AreEqual(5, mine.Count(achievement => achievement.State == AchievementState.Unlocked));
        Assert.AreEqual(3, mine.Count(achievement => achievement.State == AchievementState.Revealed));
        Assert.AreEqual(1, mine.Count(achievement => achievement.State == AchievementState.Hidden));
        Assert.IsTrue(mine.All(achievement => definitions.Any(definition => definition.Id == achievement.AchievementId)));
        Assert.AreEqual(31, mine.Single(achievement => achievement.AchievementId == definitions[2].Id).CurrentSteps);
    }

    [TestMethod]
    public async Task Rankings()
    {
        SamplePlayersApi api = Create();

        PlayerPage<RankingRow> page = await api.GetRankingAsync(new RankingQuery(), Token);
        Assert.AreEqual(50, (page.Items).Count);
        Assert.AreEqual(8423, page.TotalRecords);
        Assert.AreEqual(169, page.TotalPages);
        CollectionAssert.AreEqual(Enumerable.Range(1, 50).ToArray(), page.Items.Select(row => row.Rank).ToArray());
        Assert.IsTrue(page.Items.Zip(page.Items.Skip(1)).All(pair => pair.First.Rating >= pair.Second.Rating));
        Assert.IsTrue(page.Items.Any(row => row.ClanTag is not null));
        Assert.IsTrue(page.Items.All(row => row.Deviation < 100 && row.TotalGames > 0 && row.WinShare is > 0 and < 1));

        PlayerPage<RankingRow> third = await api.GetRankingAsync(new RankingQuery { Page = 3 }, Token);
        Assert.AreEqual(101, third.Items[0].Rank);

        string name = page.Items[3].Login;
        PlayerPage<RankingRow> found = await api.GetRankingAsync(new RankingQuery { Name = name }, Token);
        Assert.IsTrue(found.Items.Any(row => row.Login == name));
        Assert.IsTrue(found.Items.All(row => row.Login.Contains(name, StringComparison.OrdinalIgnoreCase)));

        Assert.AreEqual(0, ((await api.GetRankingAsync(new RankingQuery { Board = "tmm_9v9" }, Token)).Items).Count);
    }

    [TestMethod]
    public async Task DistributionAndRank()
    {
        SamplePlayersApi api = Create();

        IReadOnlyList<RatingBin> bins = await api.GetRatingDistributionAsync(new RankingQuery(), Token);
        Assert.AreEqual(24, (bins).Count);
        Assert.AreEqual(0, bins[0].From);
        Assert.AreEqual(2400, bins[^1].To);
        Assert.IsTrue(bins.All(bin => bin.To - bin.From == 100));
        Assert.IsTrue(Math.Abs(bins.Sum(bin => bin.Count) - 8423) < 60, "the bins add up to about the number of ratings");

        int top = await api.GetRankOfRatingAsync(new RankingQuery(), 2200, Token);
        int middle = await api.GetRankOfRatingAsync(new RankingQuery(), 1300, Token);
        int bottom = await api.GetRankOfRatingAsync(new RankingQuery(), 100, Token);
        Assert.IsTrue(top < middle);
        Assert.IsTrue(middle < bottom);
        Assert.IsTrue(top > 0);
    }

    [TestMethod]
    public async Task LeagueLayout()
    {
        SamplePlayersApi api = Create();

        IReadOnlyList<League> leagues = await api.GetLeaguesAsync(Token);
        Assert.AreEqual(1, (leagues).Count);
        Assert.AreEqual("ladder_1v1", leagues[0].TechnicalName);

        IReadOnlyList<LeagueSeason> seasons = await api.GetSeasonsAsync(Token);
        Assert.AreEqual(3, (seasons).Count);
        Assert.AreEqual(3, seasons[0].SeasonNumber, "the latest first");
        Assert.AreEqual("ladder_1v1", seasons[0].BoardTechnicalName);
        Assert.AreEqual(1, seasons.Count(season => season.IsActive(Now)));
        Assert.IsTrue(seasons[0].IsActive(Now));
        Assert.AreEqual(10, seasons[0].PlacementGames);

        IReadOnlyList<LeagueSubdivision> subdivisions = await api.GetSubdivisionsAsync(seasons[0].Id, Token);
        Assert.AreEqual(25, (subdivisions).Count);
        CollectionAssert.AreEqual(new[] { "Bronze", "Silver", "Gold", "Diamond", "Master" }, subdivisions.Select(sub => sub.Division.Name).Distinct().ToArray());
        CollectionAssert.AreEqual(new[] { "I", "II", "III", "IV", "V" }, subdivisions.Take(5).Select(sub => sub.Name).ToArray());
        Assert.AreEqual("Silver III", subdivisions[7].FullName);
        Assert.IsTrue(subdivisions.Zip(subdivisions.Skip(1)).All(pair => pair.First.MinRating < pair.Second.MinRating));

        IReadOnlyDictionary<int, int> counts = await api.GetPlayersPerSubdivisionAsync(seasons[0].Id, subdivisions, Token);
        Assert.AreEqual(25, (counts).Count);
        Assert.IsTrue(Math.Abs(counts.Values.Sum() - 8423) < 40);
    }

    [TestMethod]
    public async Task LeagueEntryStandingsAndJourney()
    {
        SamplePlayersApi api = Create();
        int player = SamplePlayersApi.SamplePlayerId;
        LeagueSeason season = (await api.GetSeasonsAsync(Token))[0];

        LeagueEntry? entry = await api.GetLeagueEntryAsync(player, season.Id, Token);
        Assert.IsNotNull(entry);
        Assert.AreEqual("Silver III", entry.Subdivision?.FullName);
        Assert.AreEqual(18, entry.Score);
        Assert.AreEqual(41, entry.GameCount);
        Assert.IsTrue(entry.ReturningPlayer);
        Assert.IsNull(await api.GetLeagueEntryAsync(player, season.Id - 1, Token));
        Assert.IsNull(await api.GetLeagueEntryAsync(Rival, season.Id, Token));
        Assert.AreEqual(1, (await api.GetActiveLeagueEntriesAsync(player, Token)).Count);

        PlayerPage<LeagueStanding> standings = await api.GetStandingsAsync(season.Id, entry.Subdivision!.Id, 1, Token);
        Assert.AreEqual(50, (standings.Items).Count);
        Assert.AreEqual(412, standings.TotalRecords);
        LeagueStanding mine = standings.Items.Single(row => row.PlayerId == player);
        Assert.AreEqual("Ashgrove", mine.Login);
        Assert.AreEqual(17, mine.Rank);
        Assert.AreEqual(entry.Score, mine.Score);
        Assert.IsTrue(standings.Items.Zip(standings.Items.Skip(1)).All(pair => pair.First.Score >= pair.Second.Score));
        Assert.IsTrue(standings.Items.All(row => !row.Login.StartsWith("Player ", StringComparison.Ordinal)), "every name is resolved");

        IReadOnlyList<LeagueJournalEntry> journey = await api.GetLeagueJourneyAsync(player, season.Id, Token);
        Assert.AreEqual(41, (journey).Count);
        Assert.IsTrue(journey.Zip(journey.Skip(1)).All(pair => pair.First.GameId < pair.Second.GameId && pair.Second.GameCount == pair.First.GameCount + 1));
        Assert.IsNull(journey[0].Before);
        Assert.IsNull(journey[0].After, "still in placement");
        Assert.AreEqual("Silver III", journey[^1].After?.FullName);
        (int promotions, int demotions) = LeagueStatistics.Moves(journey);
        Assert.AreEqual(2, promotions);
        Assert.AreEqual(0, demotions);
        Assert.AreEqual(0, (await api.GetLeagueJourneyAsync(player, season.Id - 1, Token)).Count);
    }

    [TestMethod]
    public async Task HeadToHead()
    {
        SamplePlayersApi api = Create();
        int player = SamplePlayersApi.SamplePlayerId;

        IReadOnlyList<HeadToHeadGame> games = await api.GetHeadToHeadAsync(player, Rival, null, Token);
        Assert.AreEqual(11, (games).Count);
        Assert.IsTrue(games.All(game => game.A.PlayerId == player && game.B.PlayerId == Rival && !game.SameTeam));
        Assert.IsTrue(games.All(game => game.A.Result != game.B.Result && game.MapName is { Length: > 0 } && game.Duration > TimeSpan.Zero));
        Assert.IsTrue(games.Zip(games.Skip(1)).All(pair => pair.First.StartTime >= pair.Second.StartTime));

        IReadOnlyList<HeadToHeadGame> reversed = await api.GetHeadToHeadAsync(Rival, player, null, Token);
        Assert.AreEqual(11, (reversed).Count);
        Assert.IsTrue(reversed.All(game => game.A.PlayerId == Rival));

        Assert.AreEqual(4, (await api.GetHeadToHeadAsync(player, OtherRival, null, Token)).Count);
        Assert.AreEqual(0, (await api.GetHeadToHeadAsync(Rival, OtherRival, null, Token)).Count);
        Assert.AreEqual(0, (await api.GetHeadToHeadAsync(player, Rival, Now.AddDays(1), Token)).Count);
    }
}
