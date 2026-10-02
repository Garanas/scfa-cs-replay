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
/// TODO(api-attributes): the attribute and filter names below follow the faf-java-api models
/// but could not be verified live while building this (reads require an OAuth token). Verify
/// them against real responses after the first successful login; see TODO.md.
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
        if (query.PlayedAfter is { } after)
        {
            filters.Add($"startTime=ge={after:yyyy-MM-dd}T00:00:00Z");
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

            // The displayed rating is the conservative estimate: mean - 3 * deviation.
            int? rating = stats.GetNumber("beforeMean") is { } mean && stats.GetNumber("beforeDeviation") is { } deviation
                ? (int)Math.Round(mean - 3 * deviation)
                : null;

            players.Add(new GamePlayer(
                player?.GetString("login") ?? "Unknown",
                (int?)stats.GetNumber("team"),
                stats.GetNumber("faction") is { } faction ? (GameFaction)(int)faction : null,
                rating,
                stats.GetString("result")));
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
            players);
    }

    /// <summary>
    /// Quotes a value for RSQL: wrapped in double quotes with inner quotes escaped.
    /// A '*' acts as wildcard.
    /// </summary>
    private static string Quote(string value)
        => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
