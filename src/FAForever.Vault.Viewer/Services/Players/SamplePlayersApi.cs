using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using FAForever.Vault.Viewer.Services.Api;
using FAForever.Vault.Viewer.Services.Players.Statistics;

namespace FAForever.Vault.Viewer.Services.Players;

/// <summary>
/// <see cref="IPlayersApi"/> over JSON:API documents embedded in the Viewer (<c>Services/Players/Sample</c>,
/// written by <c>tools/players-fixtures.py</c>), read by the same <see cref="PlayerMappers"/> as the real
/// API's answers. For development and tests without a token (<c>Players:SampleData</c>).
/// <para>
/// The fixtures describe one fictional player, <see cref="SamplePlayerId"/> ("Ashgrove"): four boards
/// with a year of rated games, a page of matches, counters, achievements, a place in the running
/// ladder season and a head-to-head list against two rivals. Dates are shifted by whole weeks to the
/// present, so the year always ends now. Any other player id gets the same data with the ratings moved
/// up or down by a fixed amount; the filters of the queries are applied to the fixtures in memory.
/// </para>
/// </summary>
public sealed partial class SamplePlayersApi(TimeProvider? timeProvider = null) : IPlayersApi
{
    /// <summary>The id of the sample player.</summary>
    public const int SamplePlayerId = 2049311;

    /// <summary>The moment the fixtures were written for; their dates are shifted from here to now.</summary>
    public static readonly DateTimeOffset FixtureAnchor = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    private const string ResourcePrefix = "players-sample/";

    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;
    private readonly Dictionary<string, JsonApiDocument> documents = [];

    /// <summary>The fixture documents the sample reads, by file name (for tests).</summary>
    public static IReadOnlyList<string> ResourceNames { get; } = [.. typeof(SamplePlayersApi).Assembly.GetManifestResourceNames()
        .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
        .Select(name => name[ResourcePrefix.Length..])
        .Order(StringComparer.Ordinal)];

    /// <inheritdoc />
    public Task<IReadOnlyList<PlayerSearchHit>> FindPlayersAsync(string text, CancellationToken cancellationToken)
    {
        text = text.Trim();
        if (text.Length == 0)
        {
            return Done<IReadOnlyList<PlayerSearchHit>>([]);
        }

        IReadOnlyList<PlayerSearchHit> byLogin = [.. PlayerMappers.MapLoginHits(Document("search-players.json")).Where(hit => Contains(hit.Login, text))];
        IReadOnlyList<PlayerSearchHit> byFormerName = [.. PlayerMappers.MapNameHits(Document("search-names.json"), byLogin).Where(hit => Contains(hit.MatchedFormerName, text))];
        return Done<IReadOnlyList<PlayerSearchHit>>([.. byLogin, .. byFormerName]);
    }

    /// <inheritdoc />
    public Task<PlayerSearchHit?> FindPlayerByNameAsync(string login, CancellationToken cancellationToken)
        => Done(PlayerMappers.MapLoginHits(Document("search-players.json"))
            .FirstOrDefault(hit => string.Equals(hit.Login, login.Trim(), StringComparison.OrdinalIgnoreCase)));

    /// <inheritdoc />
    public Task<PlayerProfile?> GetProfileAsync(int playerId, CancellationToken cancellationToken)
    {
        if (playerId == SamplePlayerId)
        {
            return Done(PlayerMappers.MapProfile(Document("player-2049311.json")));
        }

        // The other players of the fixtures have a name and a clan tag, nothing more.
        PlayerSearchHit? hit = PlayerMappers.MapLoginHits(Document("search-players.json")).FirstOrDefault(candidate => candidate.Id == playerId);
        return Done(hit is null ? null : new PlayerProfile(hit.Id, hit.Login, null, hit.ClanTag is { } tag ? new ClanInfo(0, $"Clan {tag}", tag) : null, []));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Leaderboard>> GetLeaderboardsAsync(CancellationToken cancellationToken)
        => Done(PlayerMappers.MapLeaderboards(Document("leaderboards.json")));

    /// <inheritdoc />
    public Task<IReadOnlyList<PlayerRating>> GetRatingsAsync(int playerId, CancellationToken cancellationToken)
    {
        int offset = RatingOffset(playerId);
        return Done<IReadOnlyList<PlayerRating>>([.. PlayerMappers.MapRatings(Document("ratings.json"))
            .Select(rating => offset == 0 ? rating : rating with { Mean = rating.Mean + offset, TotalGames = Math.Max(1, rating.TotalGames * (100 + offset % 40) / 100), WonGames = Math.Max(0, rating.WonGames * (100 + offset % 40) / 100) })]);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RatingJournalEntry>> GetRatingHistoryAsync(int playerId, string boardTechnicalName, DateTimeOffset? since, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        string name = $"rating-journal-{boardTechnicalName}.json";
        if (!ResourceNames.Contains(name))
        {
            return Done<IReadOnlyList<RatingJournalEntry>>([]);
        }

        int offset = RatingOffset(playerId);
        IReadOnlyList<RatingJournalEntry> entries = [.. PlayerMappers.MapJournal(Document(name), SamplePlayerId)
            .Where(entry => since is null || entry.Date >= since)
            .Select(entry => offset == 0 ? entry : entry with { MeanBefore = entry.MeanBefore + offset, MeanAfter = entry.MeanAfter + offset })];
        progress?.Report(entries.Count);
        return Done(entries);
    }

    /// <inheritdoc />
    public Task<PlayerPage<PlayerMatch>> GetMatchesAsync(int playerId, MatchQuery query, CancellationToken cancellationToken)
    {
        PlayerMatch[] matches = [.. Sorted(Filter(AllFixtureMatches(), query), query.Sort)];
        int page = Math.Max(query.Page, 1);
        int size = Math.Max(query.PageSize, 1);
        return Done(new PlayerPage<PlayerMatch>([.. matches.Skip((page - 1) * size).Take(size)], page, Math.Max(1, (matches.Length + size - 1) / size), matches.Length));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PlayerMatch>> GetAllMatchesAsync(int playerId, MatchQuery query, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        IReadOnlyList<PlayerMatch> matches = [.. Sorted(Filter(AllFixtureMatches(), query), MatchSort.Newest).Select(match => match with { Players = [] })];
        progress?.Report(matches.Count);
        return Done(matches);
    }

    /// <inheritdoc />
    public Task<PlayerEventCounts> GetEventCountsAsync(int playerId, CancellationToken cancellationToken)
        => Done(PlayerMappers.MapEventCounts(Document("events.json")));

    /// <inheritdoc />
    public Task<IReadOnlyList<AchievementDefinition>> GetAchievementsAsync(CancellationToken cancellationToken)
        => Done(PlayerMappers.MapAchievements(Document("achievements.json")));

    /// <inheritdoc />
    public Task<IReadOnlyList<PlayerAchievement>> GetPlayerAchievementsAsync(int playerId, CancellationToken cancellationToken)
        => Done(PlayerMappers.MapPlayerAchievements(Document("player-achievements.json")));

    /// <inheritdoc />
    public Task<PlayerPage<RankingRow>> GetRankingAsync(RankingQuery query, CancellationToken cancellationToken)
    {
        string name = $"ranking-{query.Board}.json";
        if (!ResourceNames.Contains(name))
        {
            return Done(PlayerPage<RankingRow>.Empty);
        }

        // One page of 50 rows stands for every page; a name filter narrows it.
        PlayerPage<RankingRow> page = PlayerMappers.MapRanking(Document(name), query.Page, query.PageSize);
        return Done(string.IsNullOrWhiteSpace(query.Name)
            ? page
            : page with { Items = [.. page.Items.Where(row => Contains(row.Login, query.Name.Trim()))], TotalPages = 1, TotalRecords = page.Items.Count(row => Contains(row.Login, query.Name.Trim())) });
    }

    /// <inheritdoc />
    public async Task<int> GetRankOfRatingAsync(RankingQuery query, int rating, CancellationToken cancellationToken)
    {
        IReadOnlyList<RatingBin> bins = await GetRatingDistributionAsync(query, cancellationToken);
        double better = 0;
        foreach (RatingBin bin in bins)
        {
            if (bin.From >= rating)
            {
                better += bin.Count;
            }
            else if (bin.To > rating)
            {
                better += bin.Count * (double)(bin.To - rating) / (bin.To - bin.From);
            }
        }
        return RankStatistics.RankFromCount((int)Math.Round(better));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RatingBin>> GetRatingDistributionAsync(RankingQuery query, CancellationToken cancellationToken)
    {
        using JsonDocument file = JsonDocument.Parse(Text("distribution.json"));
        if (!file.RootElement.TryGetProperty(query.Board, out JsonElement counts))
        {
            return Done<IReadOnlyList<RatingBin>>([.. Enumerable.Range(0, RatingBinning.Count).Select(index => new RatingBin(index * RatingBinning.Width, (index + 1) * RatingBinning.Width, 0))]);
        }

        return Done<IReadOnlyList<RatingBin>>([.. counts.EnumerateArray().Select((element, index) => new RatingBin(
            index * RatingBinning.Width,
            (index + 1) * RatingBinning.Width,
            PlayerMappers.MapCount(JsonApiDocument.Parse(element.GetRawText()))))]);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<League>> GetLeaguesAsync(CancellationToken cancellationToken)
        => Done(PlayerMappers.MapLeagues(Document("leagues.json")));

    /// <inheritdoc />
    public Task<IReadOnlyList<LeagueSeason>> GetSeasonsAsync(CancellationToken cancellationToken)
        => Done(PlayerMappers.MapSeasons(Document("seasons.json")));

    /// <inheritdoc />
    public Task<IReadOnlyList<LeagueSubdivision>> GetSubdivisionsAsync(int seasonId, CancellationToken cancellationToken)
        => Done(PlayerMappers.MapSubdivisions(Document("subdivisions.json"))); // every season has the layout of the running one

    /// <inheritdoc />
    public Task<LeagueEntry?> GetLeagueEntryAsync(int playerId, int seasonId, CancellationToken cancellationToken)
        => Done(playerId == SamplePlayerId ? PlayerMappers.MapLeagueEntries(Document("league-entries.json")).FirstOrDefault(entry => entry.SeasonId == seasonId) : null);

    /// <inheritdoc />
    public async Task<IReadOnlyList<LeagueEntry>> GetActiveLeagueEntriesAsync(int playerId, CancellationToken cancellationToken)
    {
        if (playerId != SamplePlayerId)
        {
            return [];
        }

        DateTimeOffset now = time.GetUtcNow();
        HashSet<int> active = [.. (await GetSeasonsAsync(cancellationToken)).Where(season => season.IsActive(now)).Select(season => season.Id)];
        return [.. PlayerMappers.MapLeagueEntries(Document("league-entries.json")).Where(entry => active.Contains(entry.SeasonId))];
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<int, int>> GetPlayersPerSubdivisionAsync(int seasonId, IReadOnlyList<LeagueSubdivision> subdivisions, CancellationToken cancellationToken)
    {
        using JsonDocument file = JsonDocument.Parse(Text("league-counts.json"));
        Dictionary<int, int> counts = [];
        foreach (LeagueSubdivision subdivision in subdivisions)
        {
            counts[subdivision.Id] = file.RootElement.TryGetProperty(subdivision.Id.ToString(CultureInfo.InvariantCulture), out JsonElement document)
                ? PlayerMappers.MapCount(JsonApiDocument.Parse(document.GetRawText()))
                : 0;
        }
        return Done<IReadOnlyDictionary<int, int>>(counts);
    }

    /// <inheritdoc />
    public Task<PlayerPage<LeagueStanding>> GetStandingsAsync(int seasonId, int subdivisionId, int page, CancellationToken cancellationToken)
        => Done(PlayerMappers.MapStandings(Document("league-standings.json"), Document("league-players.json"), page, 50)); // one page of 50 stands for every subdivision

    /// <inheritdoc />
    public Task<IReadOnlyList<LeagueJournalEntry>> GetLeagueJourneyAsync(int playerId, int seasonId, CancellationToken cancellationToken)
        => Done<IReadOnlyList<LeagueJournalEntry>>(playerId == SamplePlayerId && seasonId == RunningSeasonId() ? PlayerMappers.MapLeagueJournal(Document("league-journal.json")) : []);

    /// <inheritdoc />
    public Task<IReadOnlyList<HeadToHeadGame>> GetHeadToHeadAsync(int playerA, int playerB, DateTimeOffset? since, CancellationToken cancellationToken)
        => Done<IReadOnlyList<HeadToHeadGame>>([.. PlayerMappers.MapHeadToHead(Document("head-to-head.json"), playerA, playerB).Where(game => since is null || game.StartTime >= since)]);

    private static Task<T> Done<T>(T value) => Task.FromResult(value);

    private static bool Contains(string? value, string text) => value?.Contains(text, StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>The other players' ratings sit this much above or below the sample player's.</summary>
    private static int RatingOffset(int playerId) => playerId == SamplePlayerId ? 0 : (int)(Math.Abs((long)playerId * 7) % 301) - 150;

    private static int RunningSeasonId() => 9;

    private IReadOnlyList<PlayerMatch> AllFixtureMatches() => PlayerMappers.MapMatches(Document("matches.json"), SamplePlayerId);

    private static IEnumerable<PlayerMatch> Filter(IEnumerable<PlayerMatch> matches, MatchQuery query)
    {
        string? board = PlayerRequests.BoardOfMode(query.Mode);
        return matches.Where(match =>
            (query.Since is null || match.StartTime >= query.Since)
            && (board is null || match.Board == board)
            && (query.Faction is null || match.Faction == query.Faction)
            && (query.Result is null || match.Result == query.Result)
            && (string.IsNullOrWhiteSpace(query.Map) || Contains(match.MapName, query.Map.Trim()))
            && (string.IsNullOrWhiteSpace(query.WithPlayer) || match.Players.Any(player => player.PlayerId != SamplePlayerId && string.Equals(player.Login, query.WithPlayer.Trim(), StringComparison.OrdinalIgnoreCase))));
    }

    private static IEnumerable<PlayerMatch> Sorted(IEnumerable<PlayerMatch> matches, MatchSort sort) => sort switch
    {
        MatchSort.Oldest => matches.OrderBy(match => match.StartTime),
        MatchSort.Longest => matches.OrderByDescending(match => match.Duration),
        MatchSort.Shortest => matches.OrderBy(match => match.Duration),
        _ => matches.OrderByDescending(match => match.StartTime),
    };

    private JsonApiDocument Document(string name)
    {
        if (!documents.TryGetValue(name, out JsonApiDocument? document))
        {
            document = documents[name] = JsonApiDocument.Parse(Text(name));
        }
        return document;
    }

    /// <summary>The text of a fixture with its dates moved by whole weeks to the present.</summary>
    private string Text(string name)
    {
        using Stream stream = typeof(SamplePlayersApi).Assembly.GetManifestResourceStream(ResourcePrefix + name)
            ?? throw new FileNotFoundException($"The sample fixture {name} is not embedded.");
        using StreamReader reader = new(stream);
        string text = reader.ReadToEnd();

        int weeks = (int)Math.Floor((time.GetUtcNow() - FixtureAnchor).TotalDays / 7);
        if (weeks <= 0)
        {
            return text;
        }

        TimeSpan shift = TimeSpan.FromDays(7 * weeks);
        return Timestamp().Replace(text, match => DateTimeOffset.Parse(match.Value, CultureInfo.InvariantCulture).Add(shift).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
    }

    [GeneratedRegex(@"\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ")]
    private static partial Regex Timestamp();
}
