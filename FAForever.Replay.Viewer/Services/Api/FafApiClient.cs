using System.Net;
using System.Net.Http.Headers;
using FAForever.Replay.Viewer.Services.Auth;
using Microsoft.Extensions.Configuration;

namespace FAForever.Replay.Viewer.Services.Api;

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
        if (!string.IsNullOrWhiteSpace(query.PlayerName))
        {
            filters.Add($"playerStats.player.login=={Quote(query.PlayerName.Trim())}");
        }
        if (!string.IsNullOrWhiteSpace(query.MapName))
        {
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

            int? rating = FAForever.Replay.ReplayPlayerOptions.DisplayRating(stats.GetNumber("beforeMean"), stats.GetNumber("beforeDeviation"));

            players.Add(new GamePlayer(
                player?.GetString("login") ?? "Unknown",
                stats.GetInt32("team"),
                stats.GetInt32("faction") is { } faction && faction is >= 1 and <= 5 ? (FAForever.Replay.Faction)faction : null,
                rating,
                stats.GetString("result"))
            {
                RatingAfter = FAForever.Replay.ReplayPlayerOptions.DisplayRating(stats.GetNumber("afterMean"), stats.GetNumber("afterDeviation")),
                Color = FAForever.Replay.GameColors.ToCss(stats.GetInt32("color")),
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
            mapVersion?.GetString("thumbnailUrlSmall"),
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
