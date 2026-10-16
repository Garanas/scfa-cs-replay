using FAForever.Vault.Viewer.Services.Api;
using FAForever.Vault.Viewer.Services.Auth;
using Microsoft.Extensions.Configuration;

namespace FAForever.Vault.Viewer.Services.Players;

/// <summary>
/// <see cref="IPlayersApi"/> over api.faforever.com, with the signed-in player's token. The requests
/// are in <see cref="PlayerRequests"/>, the reading in <see cref="PlayerMappers"/>. Slow-changing lists
/// (leaderboards, achievements, leagues, seasons) and the rating distributions are kept for the life
/// of the app. The requests were checked against the live API on 2026-10-16 (<c>docs/vault/players-plan.md</c>).
/// </summary>
public sealed class FafPlayersApi(HttpClient http, AuthService auth, IConfiguration configuration) : IPlayersApi
{
    private const int StandingsPageSize = 50;

    private Task<IReadOnlyList<Leaderboard>>? leaderboards;
    private Task<IReadOnlyList<AchievementDefinition>>? achievements;
    private Task<IReadOnlyList<League>>? leagues;
    private Task<IReadOnlyList<LeagueSeason>>? seasons;
    private readonly Dictionary<string, Task<IReadOnlyList<RatingBin>>> distributions = [];

    private string BaseUrl => configuration["FafApi:BaseUrl"] ?? "https://api.faforever.com";

    /// <inheritdoc />
    public async Task<IReadOnlyList<PlayerSearchHit>> FindPlayersAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        Task<JsonApiDocument> byLogin = GetAsync(PlayerRequests.FindPlayers(text), cancellationToken);
        Task<JsonApiDocument> byFormerName = GetAsync(PlayerRequests.FindNames(text), cancellationToken);
        await Task.WhenAll(byLogin, byFormerName);
        return PlayerMappers.MapSearchHits(await byLogin, await byFormerName);
    }

    /// <inheritdoc />
    public async Task<PlayerSearchHit?> FindPlayerByNameAsync(string login, CancellationToken cancellationToken)
        => string.IsNullOrWhiteSpace(login) ? null : PlayerMappers.MapFirstHit(await GetAsync(PlayerRequests.PlayerByLogin(login), cancellationToken));

    /// <inheritdoc />
    public async Task<PlayerProfile?> GetProfileAsync(int playerId, CancellationToken cancellationToken)
        => PlayerMappers.MapProfile(await GetAsync(PlayerRequests.Profile(playerId), cancellationToken));

    /// <inheritdoc />
    public Task<IReadOnlyList<Leaderboard>> GetLeaderboardsAsync(CancellationToken cancellationToken)
        => leaderboards ??= Cached(async () => PlayerMappers.MapLeaderboards(await GetAsync(PlayerRequests.Leaderboards(), cancellationToken)), () => leaderboards = null);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PlayerRating>> GetRatingsAsync(int playerId, CancellationToken cancellationToken)
        => PlayerMappers.MapRatings(await GetAsync(PlayerRequests.Ratings(playerId), cancellationToken));

    /// <inheritdoc />
    public Task<IReadOnlyList<RatingJournalEntry>> GetRatingHistoryAsync(int playerId, string boardTechnicalName, DateTimeOffset? since, IProgress<int>? progress, CancellationToken cancellationToken)
        => LoopAsync(
            page => PlayerRequests.RatingJournal(playerId, boardTechnicalName, since, page),
            document => PlayerMappers.MapJournal(document, playerId),
            progress,
            cancellationToken);

    /// <inheritdoc />
    public async Task<PlayerPage<PlayerMatch>> GetMatchesAsync(int playerId, MatchQuery query, CancellationToken cancellationToken)
    {
        JsonApiDocument document = await GetAsync(PlayerRequests.Matches(playerId, query), cancellationToken);
        return new PlayerPage<PlayerMatch>(PlayerMappers.MapMatches(document, playerId), Math.Max(query.Page, 1), document.TotalPages, document.TotalRecords);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PlayerMatch>> GetAllMatchesAsync(int playerId, MatchQuery query, IProgress<int>? progress, CancellationToken cancellationToken)
        => LoopAsync(
            page => PlayerRequests.MatchesForCharts(playerId, query, page),
            document => PlayerMappers.MapMatches(document, playerId),
            progress,
            cancellationToken);

    /// <inheritdoc />
    public async Task<PlayerEventCounts> GetEventCountsAsync(int playerId, CancellationToken cancellationToken)
    {
        IReadOnlyList<PlayerEventCounts> pages = await LoopAsync<PlayerEventCounts>(
            page => PlayerRequests.EventCounts(playerId, page),
            document => [PlayerMappers.MapEventCounts(document)],
            null,
            cancellationToken);
        return new PlayerEventCounts(pages.SelectMany(page => page.Counts).ToDictionary(count => count.Key, count => count.Value));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AchievementDefinition>> GetAchievementsAsync(CancellationToken cancellationToken)
        => achievements ??= Cached(async () => PlayerMappers.MapAchievements(await GetAsync(PlayerRequests.Achievements(), cancellationToken)), () => achievements = null);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PlayerAchievement>> GetPlayerAchievementsAsync(int playerId, CancellationToken cancellationToken)
        => PlayerMappers.MapPlayerAchievements(await GetAsync(PlayerRequests.PlayerAchievements(playerId), cancellationToken));

    /// <inheritdoc />
    public async Task<PlayerPage<RankingRow>> GetRankingAsync(RankingQuery query, CancellationToken cancellationToken)
        => PlayerMappers.MapRanking(await GetAsync(PlayerRequests.Ranking(query), cancellationToken), query.Page, query.PageSize);

    /// <inheritdoc />
    public async Task<int> GetRankOfRatingAsync(RankingQuery query, int rating, CancellationToken cancellationToken)
        => Statistics.RankStatistics.RankFromCount(PlayerMappers.MapCount(await GetAsync(PlayerRequests.BetterRatings(query, rating), cancellationToken)));

    /// <inheritdoc />
    public Task<IReadOnlyList<RatingBin>> GetRatingDistributionAsync(RankingQuery query, CancellationToken cancellationToken)
    {
        string key = $"{query.Board}|{query.SettledOnly}|{query.ActiveSince:O}";
        if (!distributions.TryGetValue(key, out Task<IReadOnlyList<RatingBin>>? cached))
        {
            cached = distributions[key] = Cached(() => ReadDistributionAsync(query, cancellationToken), () => distributions.Remove(key));
        }
        return cached;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<League>> GetLeaguesAsync(CancellationToken cancellationToken)
        => leagues ??= Cached(async () => PlayerMappers.MapLeagues(await GetAsync(PlayerRequests.Leagues(), cancellationToken)), () => leagues = null);

    /// <inheritdoc />
    public Task<IReadOnlyList<LeagueSeason>> GetSeasonsAsync(CancellationToken cancellationToken)
        => seasons ??= Cached(async () => PlayerMappers.MapSeasons(await GetAsync(PlayerRequests.Seasons(), cancellationToken)), () => seasons = null);

    /// <inheritdoc />
    public async Task<IReadOnlyList<LeagueSubdivision>> GetSubdivisionsAsync(int seasonId, CancellationToken cancellationToken)
        => PlayerMappers.MapSubdivisions(await GetAsync(PlayerRequests.Subdivisions(seasonId), cancellationToken));

    /// <inheritdoc />
    public async Task<LeagueEntry?> GetLeagueEntryAsync(int playerId, int seasonId, CancellationToken cancellationToken)
        => PlayerMappers.MapLeagueEntries(await GetAsync(PlayerRequests.LeagueEntry(playerId, seasonId), cancellationToken)).FirstOrDefault();

    /// <inheritdoc />
    public async Task<IReadOnlyList<LeagueEntry>> GetActiveLeagueEntriesAsync(int playerId, CancellationToken cancellationToken)
        => PlayerMappers.MapLeagueEntries(await GetAsync(PlayerRequests.ActiveLeagueEntries(playerId, DateTimeOffset.UtcNow), cancellationToken));

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<int, int>> GetPlayersPerSubdivisionAsync(int seasonId, IReadOnlyList<LeagueSubdivision> subdivisions, CancellationToken cancellationToken)
    {
        Task<JsonApiDocument>[] requests = [.. subdivisions.Select(subdivision => GetAsync(PlayerRequests.PlayersInSubdivision(seasonId, subdivision.Id), cancellationToken))];
        JsonApiDocument[] documents = await Task.WhenAll(requests);
        Dictionary<int, int> counts = [];
        for (int i = 0; i < subdivisions.Count; i++)
        {
            counts[subdivisions[i].Id] = PlayerMappers.MapCount(documents[i]);
        }
        return counts;
    }

    /// <inheritdoc />
    public async Task<PlayerPage<LeagueStanding>> GetStandingsAsync(int seasonId, int subdivisionId, int page, CancellationToken cancellationToken)
    {
        JsonApiDocument scores = await GetAsync(PlayerRequests.Standings(seasonId, subdivisionId, page, StandingsPageSize), cancellationToken);
        IReadOnlyList<int> ids = PlayerMappers.StandingPlayerIds(scores);
        JsonApiDocument? players = ids.Count > 0 ? await GetAsync(PlayerRequests.PlayersById(ids), cancellationToken) : null;
        return PlayerMappers.MapStandings(scores, players, page, StandingsPageSize);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<LeagueJournalEntry>> GetLeagueJourneyAsync(int playerId, int seasonId, CancellationToken cancellationToken)
        => LoopAsync(page => PlayerRequests.LeagueJourney(playerId, seasonId, page), PlayerMappers.MapLeagueJournal, null, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<HeadToHeadGame>> GetHeadToHeadAsync(int playerA, int playerB, DateTimeOffset? since, CancellationToken cancellationToken)
    {
        IReadOnlyList<HeadToHeadGame> games = await LoopAsync(
            page => PlayerRequests.HeadToHead(playerA, playerB, since, page),
            document => PlayerMappers.MapHeadToHead(document, playerA, playerB),
            null,
            cancellationToken);
        return [.. games.OrderByDescending(game => game.StartTime ?? DateTimeOffset.MinValue)];
    }

    private async Task<IReadOnlyList<RatingBin>> ReadDistributionAsync(RankingQuery query, CancellationToken cancellationToken)
    {
        Task<JsonApiDocument>[] requests = [.. Enumerable.Range(0, RatingBinning.Count)
            .Select(index => GetAsync(PlayerRequests.RatingsBetween(query, index * RatingBinning.Width, (index + 1) * RatingBinning.Width), cancellationToken))];
        JsonApiDocument[] documents = await Task.WhenAll(requests);
        return [.. documents.Select((document, index) => new RatingBin(index * RatingBinning.Width, (index + 1) * RatingBinning.Width, PlayerMappers.MapCount(document)))];
    }

    /// <summary>
    /// Reads pages of <see cref="PlayerRequests.LoopPageSize"/> rows until the totals of the answer
    /// (<c>meta.page.totalPages</c>, asked with <c>page[totals]</c>) say the last page is read, reporting the
    /// number of rows so far. At most <see cref="PlayerRequests.MaxLoopPages"/> pages.
    /// </summary>
    private async Task<IReadOnlyList<T>> LoopAsync<T>(
        Func<int, string> request,
        Func<JsonApiDocument, IReadOnlyList<T>> map,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        List<T> all = [];
        int total = 0;
        for (int page = 1; page <= PlayerRequests.MaxLoopPages; page++)
        {
            JsonApiDocument document = await GetAsync(request(page), cancellationToken);
            all.AddRange(map(document));
            total += document.Data.Count;
            progress?.Report(total);
            if (!HasMorePages(document, page))
            {
                break;
            }
        }
        return all;
    }

    /// <summary>
    /// Whether a page after <paramref name="page"/> exists: by <c>totalPages</c> when the answer has
    /// the totals, else (without them) when this page was full. An empty page is always the last.
    /// </summary>
    public static bool HasMorePages(JsonApiDocument document, int page)
        => document.Data.Count > 0 && (document.TotalPages is { } pages ? page < pages : document.Data.Count >= PlayerRequests.LoopPageSize);

    /// <summary>Starts a request whose answer is kept; a failure is forgotten so the next call tries again.</summary>
    private static async Task<IReadOnlyList<T>> Cached<T>(Func<Task<IReadOnlyList<T>>> read, Action forget)
    {
        await Task.Yield(); // a failure that comes at once must not be stored before the caller has it
        try
        {
            return await read();
        }
        catch
        {
            forget();
            throw;
        }
    }

    private async Task<JsonApiDocument> GetAsync(string request, CancellationToken cancellationToken)
        => JsonApiDocument.Parse(await FafApiClient.GetAsync(http, auth, BaseUrl + request, cancellationToken));
}
