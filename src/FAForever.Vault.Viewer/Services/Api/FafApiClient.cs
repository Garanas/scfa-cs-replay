using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FAForever.Vault.Viewer.Services.Auth;
using Microsoft.Extensions.Configuration;

namespace FAForever.Vault.Viewer.Services.Api;

/// <summary>
/// Thrown when the FAF API cannot be used because there is no valid session (never signed in,
/// or the refresh token expired).
/// </summary>
public sealed class NotAuthenticatedException : Exception;

/// <summary>
/// Typed client for api.faforever.com (JSON:API/Elide with RSQL filters). The API allows
/// cross-origin requests, so it is called directly from the browser with a Bearer token.
///
/// The attribute and filter names were verified against live responses on 2026-10-03
/// (search by login, totals, map/mod includes, ratings and factions). The cards also use
/// game.validity/victoryCondition/replayTicks/replayAvailable and gamePlayerStats.result,
/// afterMean/afterDeviation, color and ai: names taken from the published schema
/// (/v3/api-docs) and read defensively.
/// </summary>
public sealed class FafApiClient(HttpClient http, AuthService auth, IConfiguration configuration)
{
    private string BaseUrl => configuration["FafApi:BaseUrl"] ?? "https://api.faforever.com";

    public async Task<GameSearchResult> SearchGamesAsync(GameSearchQuery query, CancellationToken cancellationToken)
    {
        List<string> filters = [];
        if (query.FinishedOnly)
        {
            filters.Add("endTime=isnull=false");
        }
        if (query.RatedOnly)
        {
            filters.Add("validity==VALID");
        }
        if (!string.IsNullOrWhiteSpace(query.PlayerName))
        {
            filters.Add($"playerStats.player.login=={Quote(query.PlayerName.Trim())}");
        }
        if (!string.IsNullOrWhiteSpace(query.MapName))
        {
            // Slow for popular maps combined with sort=-startTime (up to 20 s uncached on 2026-10-06),
            // probably a missing index on game_stats (mapId, startTime): FAForever/faf-java-api#1182.
            filters.Add($"mapVersion.map.displayName=={Quote("*" + query.MapName.Trim() + "*")}");
        }
        if (!string.IsNullOrWhiteSpace(query.FeaturedMod))
        {
            filters.Add($"featuredMod.technicalName=={Quote(query.FeaturedMod)}");
        }
        if (query.PlayedWindow is { } window)
        {
            filters.Add($"startTime=ge={window.From:yyyy-MM-dd}T00:00:00Z");
            filters.Add($"startTime=lt={window.To.AddDays(1):yyyy-MM-dd}T00:00:00Z");
        }

        string url = $"{BaseUrl}/data/game"
            + "?include=playerStats,playerStats.player,mapVersion,mapVersion.map,featuredMod"
            + "&sort=-startTime"
            + $"&page[size]={query.PageSize}"
            + $"&page[number]={Math.Max(query.Page, 1)}"
            + "&page[totals]";
        if (filters.Count > 0)
        {
            url += "&filter=" + Uri.EscapeDataString(string.Join(";", filters));
        }

        JsonApiDocument document = JsonApiDocument.Parse(await GetAsync(url, cancellationToken));

        List<GameSummary> games = [];
        foreach (JsonApiResource game in document.Data)
        {
            games.Add(MapGame(game, document));
        }

        return new GameSearchResult(games, Math.Max(query.Page, 1), document.TotalPages, document.TotalRecords);
    }

    /// <summary>
    /// Maps of the vault, with their latest version. Maps whose latest version is hidden are always
    /// left out: their author took them down, and the original game maps (Seton's Clutch,
    /// <c>scmp_009</c>) are hidden too, as they come with the game. Every filter and sort here
    /// answered in 0.1 to 0.2 s uncached on 2026-10-06, for about 9,300 visible maps.
    /// </summary>
    public async Task<MapSearchResult> SearchMapsAsync(MapSearchQuery query, CancellationToken cancellationToken)
    {
        List<string> filters = ["latestVersion.hidden==false"];
        if (!string.IsNullOrWhiteSpace(query.Name))
        {
            filters.Add($"displayName=={Quote("*" + query.Name.Trim() + "*")}");
        }
        if (!string.IsNullOrWhiteSpace(query.Author))
        {
            filters.Add($"author.login=={Quote(query.Author.Trim())}");
        }
        if (query.Sizes.Count > 0)
        {
            filters.Add($"latestVersion.width=in=({string.Join(",", query.Sizes.Select(size => Quote(size.ToString(CultureInfo.InvariantCulture))))})");
        }
        if (query.Players.Count > 0)
        {
            filters.Add($"latestVersion.maxPlayers=in=({string.Join(",", query.Players.Select(players => Quote(players.ToString(CultureInfo.InvariantCulture))))})");
        }
        if (query.RankedOnly)
        {
            filters.Add("latestVersion.ranked==true");
        }
        if (query.FeaturedOnly)
        {
            filters.Add("recommended==true");
        }

        string sort = query.Sort switch
        {
            MapSortOrder.BestRated => "-reviewsSummary.lowerBound",
            MapSortOrder.Newest => "-latestVersion.createTime",
            MapSortOrder.Name => "displayName",
            _ => "-gamesPlayed",
        };

        string url = $"{BaseUrl}/data/map"
            + "?include=latestVersion,author,reviewsSummary"
            + $"&sort={sort}"
            + $"&page[size]={query.PageSize}"
            + $"&page[number]={Math.Max(query.Page, 1)}"
            + "&page[totals]"
            + "&filter=" + Uri.EscapeDataString(string.Join(";", filters));

        JsonApiDocument document = JsonApiDocument.Parse(await GetAsync(url, cancellationToken));

        List<MapSummary> maps = [.. document.Data.Select(map => MapMap(map, document.FindIncluded(map.Relationship("latestVersion")), document))];
        return new MapSearchResult(maps, Math.Max(query.Page, 1), document.TotalPages, document.TotalRecords);
    }

    /// <summary>
    /// The current map pools of the matchmaker, per queue, in one request (0.3 s, 23 pools in 5 queues
    /// on 2026-10-06). A pool holds a version of a map, which may be hidden in the vault: the pool is
    /// what the matchmaker plays, so those stay in. Generated maps have only their parameters.
    /// </summary>
    public async Task<IReadOnlyList<LadderQueue>> GetLadderPoolsAsync(CancellationToken cancellationToken)
    {
        const string Assignments = "mapPool.mapPoolAssignments";
        const string Versions = Assignments + ".mapVersion";
        string url = $"{BaseUrl}/data/matchmakerQueueMapPool"
            + $"?include=matchmakerQueue,mapPool,{Assignments},{Versions},{Versions}.map,{Versions}.map.author,{Versions}.map.reviewsSummary"
            + "&page[size]=100";

        JsonApiDocument document = JsonApiDocument.Parse(await GetAsync(url, cancellationToken));

        List<(JsonApiResource Queue, LadderPool Pool)> pools = [];
        foreach (JsonApiResource queuePool in document.Data)
        {
            if (document.FindIncluded(queuePool.Relationship("matchmakerQueue")) is not { } queue
                || document.FindIncluded(queuePool.Relationship("mapPool")) is not { } pool)
            {
                continue;
            }

            List<LadderPoolEntry> entries = [];
            foreach ((string Type, string Id) reference in pool.Relationships("mapPoolAssignments"))
            {
                if (document.FindIncluded(reference) is not { } assignment)
                {
                    continue;
                }

                int weight = assignment.GetInt32("weight") ?? 1;
                if (document.FindIncluded(assignment.Relationship("mapVersion")) is { } version
                    && document.FindIncluded(version.Relationship("map")) is { } map)
                {
                    entries.Add(new LadderPoolEntry(MapMap(map, version, document), null, weight));
                }
                else if (assignment.GetObject("mapParams") is { } parameters)
                {
                    entries.Add(new LadderPoolEntry(null, new GeneratedMapParams(
                        parameters.TryGetProperty("size", out JsonElement size) && size.TryGetInt32(out int units) ? units : null,
                        parameters.TryGetProperty("spawns", out JsonElement spawns) && spawns.TryGetInt32(out int count) ? count : null,
                        parameters.TryGetProperty("version", out JsonElement generator) ? generator.GetString() : null), weight));
                }
            }

            string name = pool.GetString("name") ?? $"Pool {pool.Id}";
            pools.Add((queue, new LadderPool(name, LadderPool.LabelOf(name), queuePool.GetNumber("minRating"), queuePool.GetNumber("maxRating"), entries)));
        }

        return [.. pools
            .GroupBy(entry => entry.Queue.Id)
            .Select(group => new LadderQueue(
                group.First().Queue.GetString("technicalName") ?? $"queue{group.Key}",
                group.First().Queue.GetInt32("teamSize") ?? 0,
                [.. group.Select(entry => entry.Pool).OrderBy(pool => pool.MinRating ?? double.MinValue)]))
            .OrderBy(queue => queue.TeamSize)
            .ThenBy(queue => queue.TechnicalName, StringComparer.Ordinal)];
    }

    /// <summary>A map resource and one of its versions (the latest, or the one in a pool) as a card.</summary>
    private static MapSummary MapMap(JsonApiResource map, JsonApiResource? version, JsonApiDocument document)
    {
        JsonApiResource? author = document.FindIncluded(map.Relationship("author"));
        JsonApiResource? reviews = document.FindIncluded(map.Relationship("reviewsSummary"));
        string? folder = version?.GetString("folderName");

        return new MapSummary(int.TryParse(map.Id, out int id) ? id : 0, map.GetString("displayName") ?? folder ?? $"Map {map.Id}", author?.GetString("login"))
        {
            GamesPlayed = map.GetInt32("gamesPlayed") ?? 0,
            Featured = map.GetBoolean("recommended") ?? false,
            Version = version?.GetInt32("version"),
            FolderName = folder,
            Description = version?.GetString("description"),
            MaxPlayers = version?.GetInt32("maxPlayers"),
            Width = version?.GetInt32("width"),
            Height = version?.GetInt32("height"),
            Ranked = version?.GetBoolean("ranked"),
            PreviewUrl = MapPreviews.Url(folder, version?.GetString("thumbnailUrlLarge")),
            UploadedAt = version?.GetDateTimeOffset("createTime"),
            AverageScore = reviews?.GetNumber("averageScore"),
            Reviews = reviews?.GetInt32("reviews") ?? 0,
        };
    }

    private async Task<string> GetAsync(string url, CancellationToken cancellationToken)
    {
        string accessToken = await auth.GetValidAccessTokenAsync() ?? throw new NotAuthenticatedException();

        using HttpRequestMessage request = new(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.api+json"));

        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new NotAuthenticatedException();
        }
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static GameSummary MapGame(JsonApiResource game, JsonApiDocument document)
    {
        JsonApiResource? mapVersion = document.FindIncluded(game.Relationship("mapVersion"));
        JsonApiResource? map = document.FindIncluded(mapVersion?.Relationship("map"));
        JsonApiResource? featuredMod = document.FindIncluded(game.Relationship("featuredMod"));

        List<GamePlayer> players = [];
        foreach ((string Type, string Id) reference in game.Relationships("playerStats"))
        {
            if (document.FindIncluded(reference) is not { } stats)
            {
                continue;
            }

            JsonApiResource? player = document.FindIncluded(stats.Relationship("player"));

            int? rating = FAForever.FileFormats.Replay.ReplayPlayerOptions.DisplayRating(stats.GetNumber("beforeMean"), stats.GetNumber("beforeDeviation"));

            players.Add(new GamePlayer(
                player?.GetString("login") ?? "Unknown",
                stats.GetInt32("team"),
                stats.GetInt32("faction") is { } faction && faction is >= 1 and <= 5 ? (FAForever.FileFormats.Blueprints.Faction)faction : null,
                rating,
                stats.GetString("result"))
            {
                RatingAfter = FAForever.FileFormats.Replay.ReplayPlayerOptions.DisplayRating(stats.GetNumber("afterMean"), stats.GetNumber("afterDeviation")),
                Color = FAForever.FileFormats.Replay.GameColors.ToCss(stats.GetInt32("color")),
                IsAi = stats.GetBoolean("ai") ?? false,
            });
        }

        players.Sort((left, right) => (left.Team ?? int.MaxValue).CompareTo(right.Team ?? int.MaxValue));

        return new GameSummary(
            int.TryParse(game.Id, out int id) ? id : 0,
            game.GetString("name") ?? $"Game {game.Id}",
            game.GetDateTimeOffset("startTime"),
            game.GetDateTimeOffset("endTime"),
            map?.GetString("displayName") ?? mapVersion?.GetString("folderName"),
            MapPreviews.Url(mapVersion?.GetString("folderName"), mapVersion?.GetString("thumbnailUrlSmall")),
            featuredMod?.GetString("displayName") ?? featuredMod?.GetString("technicalName"),
            players)
        {
            ReplayTicks = game.GetInt32("replayTicks"),
            Validity = game.GetString("validity"),
            VictoryCondition = game.GetString("victoryCondition"),
            ReplayAvailable = game.GetBoolean("replayAvailable"),
        };
    }

    /// <summary>
    /// Quotes a value for RSQL: wrapped in double quotes with inner quotes escaped.
    /// A '*' acts as wildcard.
    /// </summary>
    private static string Quote(string value)
        => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
