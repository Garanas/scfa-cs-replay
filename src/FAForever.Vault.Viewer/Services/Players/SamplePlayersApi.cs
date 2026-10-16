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
/// ladder season (and placement in the running 2v2 season) and a head-to-head list against two rivals
/// who have profiles of their own. Dates are shifted by whole weeks to the present, so the year always
/// ends now. Any other player id gets the same data with the ratings moved up or down by a fixed
/// amount; the filters of the queries are applied to the fixtures in memory. The rankings and the
/// standings are made from the rating distribution and the league counts, a row per rank, so every
/// page, board, filter and subdivision shows other rows.
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
        if (ResourceNames.Contains($"player-{playerId.ToString(CultureInfo.InvariantCulture)}.json"))
        {
            return Done(PlayerMappers.MapProfile(Document($"player-{playerId.ToString(CultureInfo.InvariantCulture)}.json")));
        }

        // The other players of the fixtures without a profile of their own have a name and a clan tag, nothing more.
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
    public async Task<PlayerPage<RankingRow>> GetRankingAsync(RankingQuery query, CancellationToken cancellationToken)
    {
        if (!RankingTotals.TryGetValue(query.Board, out int total))
        {
            return PlayerPage<RankingRow>.Empty;
        }

        DateTimeOffset now = time.GetUtcNow();
        double factor = (query.SettledOnly ? SettledShare : 1) * ActiveShare(query.ActiveSince, now);
        int count = (int)Math.Round(total * factor);
        IReadOnlyList<RatingBin> bins = await GetRatingDistributionAsync(query, cancellationToken);
        int page = Math.Max(query.Page, 1);
        int size = Math.Max(query.PageSize, 1);

        // Rows are made by rank; a name narrows them to the matches among the first ones.
        int[] matching;
        if (string.IsNullOrWhiteSpace(query.Name))
        {
            matching = [.. Enumerable.Range((page - 1) * size + 1, Math.Clamp(count - (page - 1) * size, 0, size))];
        }
        else
        {
            int[] found = [.. Enumerable.Range(1, Math.Min(count, NameSearchDepth))
                .Where(rank => Contains(RankingLogin(query.Board, RankInFullList(rank, factor)), query.Name.Trim()))];
            count = found.Length;
            matching = [.. found.Skip((page - 1) * size).Take(size)];
        }

        JsonApiDocument document = RankingDocument(query, bins, matching, factor, now, count, size);
        return PlayerMappers.MapRanking(document, page, size);
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
    {
        // The fixture is one page of 50 in the sample player's subdivision; the others get the same rows
        // with other names and scores, as many as the subdivision has players (its count is the card's).
        page = Math.Max(page, 1);
        PlayerPage<LeagueStanding> template = PlayerMappers.MapStandings(Document("league-standings.json"), Document("league-players.json"), 1, StandingsPageSize);
        int? ownSubdivision = PlayerMappers.MapLeagueEntries(Document("league-entries.json")).Select(entry => entry.Subdivision?.Id).FirstOrDefault(id => id is not null);
        int total = PlayersIn(subdivisionId) ?? template.TotalRecords ?? template.Items.Count;
        int highest = PlayerMappers.MapSubdivisions(Document("subdivisions.json")).FirstOrDefault(subdivision => subdivision.Id == subdivisionId)?.HighestScore ?? 44;
        int shift = subdivisionId * 7 + (page - 1) * 13;
        bool own = subdivisionId == ownSubdivision && page == 1;

        List<LeagueStanding> rows = [];
        int first = (page - 1) * StandingsPageSize;
        for (int i = 0; i < Math.Min(StandingsPageSize, Math.Max(total - first, 0)); i++)
        {
            LeagueStanding row = template.Items[i];
            if (own)
            {
                rows.Add(row);
                continue;
            }

            string login = template.Items[(i * 7 + shift) % StandingsPageSize].Login;
            if (login == "Ashgrove")
            {
                login = "AshgroveJr";
            }
            rows.Add(new LeagueStanding(first + i + 1, 410000 + subdivisionId * 1000 + first + i, $"{login}{shift % 90 + 10}", Math.Max(0, row.Score + highest - 44 - (page - 1) * 14), row.GameCount + shift % 7));
        }
        return Done(new PlayerPage<LeagueStanding>(rows, page, Math.Max(1, (total + StandingsPageSize - 1) / StandingsPageSize), total));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<LeagueJournalEntry>> GetLeagueJourneyAsync(int playerId, int seasonId, CancellationToken cancellationToken)
        => Done<IReadOnlyList<LeagueJournalEntry>>(playerId == SamplePlayerId && seasonId == RunningSeasonId() ? PlayerMappers.MapLeagueJournal(Document("league-journal.json")) : []);

    /// <inheritdoc />
    public Task<IReadOnlyList<HeadToHeadGame>> GetHeadToHeadAsync(int playerA, int playerB, DateTimeOffset? since, CancellationToken cancellationToken)
        => Done<IReadOnlyList<HeadToHeadGame>>([.. PlayerMappers.MapHeadToHead(Document("head-to-head.json"), playerA, playerB).Where(game => since is null || game.StartTime >= since)]);

    private const int StandingsPageSize = 50;

    /// <summary>How many players have a rating on each board (the sum of the board's distribution, about).</summary>
    private static readonly Dictionary<string, int> RankingTotals = new()
    {
        ["global"] = 52_410,
        ["ladder_1v1"] = 8_423,
        ["tmm_2v2"] = 3_906,
        ["tmm_4v4_full_share"] = 2_218,
        ["tmm_4v4_share_until_death"] = 1_104,
        ["tmm_3v3"] = 1_650,
    };

    /// <summary>The share of the ratings whose deviation is below 100.</summary>
    private const double SettledShare = 0.72;

    /// <summary>A name filter searches this many of the first ranks.</summary>
    private const int NameSearchDepth = 4000;

    private static readonly string[] Adjectives = ["Grim", "Quiet", "Iron", "Swift", "Amber", "Frost", "Hollow", "Rustic", "Violet", "Lunar", "Brisk", "Cobalt", "Dusty", "Echo", "Feral", "Gilded"];
    private static readonly string[] Nouns = ["Fox", "Anvil", "Harbor", "Falcon", "Comet", "Rook", "Thistle", "Gale", "Ember", "Lynx", "Pylon", "Quartz", "Raven", "Sable", "Tundra", "Willow"];
    private static readonly string?[] ClanTags = [null, null, null, "TNK", "RFS", "GDA", "OPS", null];

    private static Task<T> Done<T>(T value) => Task.FromResult(value);

    /// <summary>The share of the ratings changed since a moment: a week, a month, a year; none means all.</summary>
    private static double ActiveShare(DateTimeOffset? since, DateTimeOffset now)
    {
        if (since is not { } start)
        {
            return 1;
        }

        double days = (now - start).TotalDays;
        return days <= 8 ? 0.12 : days <= 32 ? 0.35 : days <= 100 ? 0.6 : days <= 370 ? 0.85 : 1;
    }

    /// <summary>The position of a row of a narrowed ranking in the whole ranking: the narrowed ones are every 1/factor-th.</summary>
    private static int RankInFullList(int rank, double factor) => (int)Math.Floor((rank - 1) / factor) + 1;

    private static uint Mix(string board, int index)
    {
        uint hash = 2166136261;
        foreach (char letter in board)
        {
            hash = (hash ^ letter) * 16777619;
        }
        hash = (hash ^ (uint)index) * 16777619;
        hash ^= hash >> 15;
        hash *= 2246822519;
        hash ^= hash >> 13;
        return hash;
    }

    /// <summary>The name at a place of the whole ranking: every combination of two words once (a permutation by board), then numbered.</summary>
    private static string RankingLogin(string board, int fullRank)
    {
        int offset = (int)(Mix(board, 0) % 256);
        int word = (int)((fullRank * 97L + offset) % 256);
        int round = fullRank / 256;
        return $"{Adjectives[word % 16]}{Nouns[word / 16]}{(round == 0 ? "" : round.ToString(CultureInfo.InvariantCulture))}";
    }

    /// <summary>The rating at a rank of the whole ranking, from the bins of the distribution (the top bin first).</summary>
    private static double RatingAtRank(IReadOnlyList<RatingBin> bins, int fullRank)
    {
        int before = 0;
        for (int i = bins.Count - 1; i >= 0; i--)
        {
            RatingBin bin = bins[i];
            if (fullRank <= before + bin.Count)
            {
                return bin.To - (fullRank - before) / (double)Math.Max(bin.Count, 1) * (bin.To - bin.From);
            }
            before += bin.Count;
        }
        return 0;
    }

    /// <summary>The ratings of the given ranks as a JSON:API document, as the real ranking request answers.</summary>
    private static JsonApiDocument RankingDocument(RankingQuery query, IReadOnlyList<RatingBin> bins, int[] ranks, double factor, DateTimeOffset now, int total, int size)
    {
        System.Text.Json.Nodes.JsonArray data = [];
        System.Text.Json.Nodes.JsonArray included = [];
        int boardIndex = (int)(Mix(query.Board, 1) % 7);
        double windowHours = query.ActiveSince is { } since ? Math.Max((now - since).TotalHours, 1) : 24 * 400;
        foreach (int rank in ranks)
        {
            int fullRank = RankInFullList(rank, factor);
            uint hash = Mix(query.Board, fullRank);
            double deviation = query.SettledOnly || hash % 100 >= 28 ? 46 + hash % 50 : 100 + hash % 70;
            double display = Math.Max(RatingAtRank(bins, fullRank), 20);
            int games = 900 + (int)(hash % 3300);
            int playerId = 200000 + boardIndex * 100000 + fullRank;
            string? tag = ClanTags[(hash >> 4) % (uint)ClanTags.Length];

            System.Text.Json.Nodes.JsonObject player = new()
            {
                ["type"] = "player",
                ["id"] = playerId.ToString(CultureInfo.InvariantCulture),
                ["attributes"] = new System.Text.Json.Nodes.JsonObject { ["login"] = RankingLogin(query.Board, fullRank) },
            };
            if (tag is not null)
            {
                string clanId = (4400 + tag.Sum(letter => letter)).ToString(CultureInfo.InvariantCulture);
                string membershipId = (90000 + playerId).ToString(CultureInfo.InvariantCulture);
                player["relationships"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["clanMembership"] = new System.Text.Json.Nodes.JsonObject { ["data"] = new System.Text.Json.Nodes.JsonObject { ["type"] = "clanMembership", ["id"] = membershipId } },
                };
                included.Add(new System.Text.Json.Nodes.JsonObject
                {
                    ["type"] = "clanMembership",
                    ["id"] = membershipId,
                    ["relationships"] = new System.Text.Json.Nodes.JsonObject { ["clan"] = new System.Text.Json.Nodes.JsonObject { ["data"] = new System.Text.Json.Nodes.JsonObject { ["type"] = "clan", ["id"] = clanId } } },
                });
                included.Add(new System.Text.Json.Nodes.JsonObject
                {
                    ["type"] = "clan",
                    ["id"] = clanId,
                    ["attributes"] = new System.Text.Json.Nodes.JsonObject { ["name"] = $"Clan {tag}", ["tag"] = tag },
                });
            }
            included.Add(player);
            data.Add(new System.Text.Json.Nodes.JsonObject
            {
                ["type"] = "leaderboardRating",
                ["id"] = (700000 + boardIndex * 100000 + fullRank).ToString(CultureInfo.InvariantCulture),
                ["attributes"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["mean"] = Math.Round(display + 3 * deviation, 2),
                    ["deviation"] = deviation,
                    ["totalGames"] = games,
                    ["wonGames"] = (int)(games * (0.5 + hash % 19 / 100.0)),
                    ["updateTime"] = now.AddHours(-1 - hash % (uint)windowHours).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                },
                ["relationships"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["player"] = new System.Text.Json.Nodes.JsonObject { ["data"] = new System.Text.Json.Nodes.JsonObject { ["type"] = "player", ["id"] = playerId.ToString(CultureInfo.InvariantCulture) } },
                },
            });
        }

        System.Text.Json.Nodes.JsonObject root = new()
        {
            ["data"] = data,
            ["included"] = included,
            ["meta"] = new System.Text.Json.Nodes.JsonObject
            {
                ["page"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["totalRecords"] = total,
                    ["totalPages"] = Math.Max(1, (total + size - 1) / size),
                    ["number"] = Math.Max(query.Page, 1),
                    ["limit"] = size,
                },
            },
        };
        return JsonApiDocument.Parse(root.ToJsonString());
    }

    /// <summary>The players of a subdivision, from the counts fixture; null for a subdivision it does not hold.</summary>
    private int? PlayersIn(int subdivisionId)
    {
        using JsonDocument file = JsonDocument.Parse(Text("league-counts.json"));
        return file.RootElement.TryGetProperty(subdivisionId.ToString(CultureInfo.InvariantCulture), out JsonElement document)
            ? PlayerMappers.MapCount(JsonApiDocument.Parse(document.GetRawText()))
            : null;
    }

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
