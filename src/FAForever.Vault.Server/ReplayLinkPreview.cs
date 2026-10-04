using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.FileProviders;

/// <summary>
/// Link previews for replay pages. Discord, X, Slack and other link unfurlers read the Open Graph
/// tags of the HTML without running the app, so the Server fills them in for <c>/replay/{id}</c>:
/// the map preview and name, replay id, team layout, date and length.
/// <para>
/// The data comes from the first line of the replay file, its JSON metadata, which the vault serves
/// anonymously (the FAF API's game data needs a token). Only that line is read; the replay itself is
/// never downloaded or passed on. The cache holds public replay metadata only, never session state.
/// </para>
/// </summary>
public sealed class ReplayLinkPreview(
    IHttpClientFactory httpClientFactory,
    IMemoryCache cache,
    IWebHostEnvironment environment,
    IConfiguration configuration,
    ILogger<ReplayLinkPreview> logger)
{
    public const string HttpClientName = "FafReplays";

    private const string BlockStart = "<!-- Link preview";
    private const string BlockEnd = "<!-- /Link preview -->";

    /// <summary>The metadata line is a few hundred bytes; anything longer is not a replay.</summary>
    private const int MaxMetadataBytes = 64 * 1024;

    private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(3);

    // Replays never change, so a card can stay a long time; a failed lookup is retried soon.
    private static readonly TimeSpan CardLifetime = TimeSpan.FromDays(1);
    private static readonly TimeSpan MissingLifetime = TimeSpan.FromMinutes(5);

    // Every uncached replay costs a request to the vault: limit those per client address, so the
    // page cannot be used to hammer FAF. Over the limit the page is served without a preview.
    private static readonly PartitionedRateLimiter<string> FetchLimiter = PartitionedRateLimiter.Create<string, string>(
        address => RateLimitPartition.GetFixedWindowLimiter(address,
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1) }));

    private string ReplayUrlFormat => configuration["LinkPreview:ReplayUrl"] ?? "https://api.faforever.com/game/{0}/replay";

    private string MapPreviewUrlFormat => configuration["LinkPreview:MapPreviewUrl"] ?? "https://content.faforever.com/maps/previews/large/{0}.png";

    /// <summary>The app's entry page, with the link preview of the replay when it could be read.</summary>
    public async Task<string?> RenderAsync(int replayId, HttpContext context, CancellationToken cancellationToken)
    {
        IFileInfo index = environment.WebRootFileProvider.GetFileInfo("index.html");
        if (!index.Exists)
        {
            return null;
        }

        string html;
        await using (Stream stream = index.CreateReadStream())
        using (StreamReader reader = new(stream))
        {
            html = await reader.ReadToEndAsync(cancellationToken);
        }

        ReplayCard? card = await GetCardAsync(replayId, context.Connection.RemoteIpAddress?.ToString() ?? "unknown", cancellationToken);
        return card is null ? html : ReplaceBlock(html, RenderTags(card, context.Request.GetEncodedUrl()));
    }

    private async Task<ReplayCard?> GetCardAsync(int replayId, string clientAddress, CancellationToken cancellationToken)
    {
        string key = $"replay-card:{replayId}";
        if (cache.TryGetValue(key, out ReplayCard? cached))
        {
            return cached;
        }

        using RateLimitLease lease = FetchLimiter.AttemptAcquire(clientAddress);
        if (!lease.IsAcquired)
        {
            return null;
        }

        ReplayCard? card = await FetchCardAsync(replayId, cancellationToken);
        cache.Set(key, card, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = card is null ? MissingLifetime : CardLifetime,
            Size = 1,
        });
        return card;
    }

    private async Task<ReplayCard?> FetchCardAsync(int replayId, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(FetchTimeout);

        try
        {
            // The API redirects to the file on content.faforever.com; the range keeps it to the start.
            using HttpRequestMessage request = new(HttpMethod.Get, string.Format(CultureInfo.InvariantCulture, ReplayUrlFormat, replayId));
            request.Headers.Range = new RangeHeaderValue(0, MaxMetadataBytes - 1);

            HttpClient client = httpClientFactory.CreateClient(HttpClientName);
            using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            byte[]? line = await ReadFirstLineAsync(stream, timeout.Token);
            return line is null ? null : ParseCard(replayId, line);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException
            || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation(exception, "No link preview for replay {ReplayId}", replayId);
            return null;
        }
    }

    /// <summary>Reads up to the first newline: the JSON metadata of a .fafreplay file.</summary>
    private static async Task<byte[]?> ReadFirstLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[MaxMetadataBytes];
        int length = 0;
        while (length < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken);
            if (read == 0)
            {
                return null;
            }

            int newline = Array.IndexOf(buffer, (byte)'\n', length, read);
            if (newline >= 0)
            {
                return buffer[..newline];
            }

            length += read;
        }

        return null;
    }

    internal static ReplayCard? ParseCard(int replayId, byte[] json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        // Lobbies without a map selection record the string "None".
        string? mapFolder = String(root, "mapname") is { Length: > 0 } folder && folder != "None" ? folder : null;

        List<int> teamSizes = TeamSizes(root);
        double launchedAt = Number(root, "launched_at");
        double gameEnd = Number(root, "game_end");
        return new ReplayCard(
            replayId,
            mapFolder,
            MapDisplayName(mapFolder),
            root.TryGetProperty("num_players", out JsonElement players) && players.TryGetInt32(out int count) ? count : teamSizes.Sum(),
            teamSizes,
            launchedAt > 0 ? DateTimeOffset.FromUnixTimeSeconds((long)launchedAt) : null,
            launchedAt > 0 && gameEnd > launchedAt ? TimeSpan.FromSeconds(gameEnd - launchedAt) : null);
    }

    /// <summary>How many players each team had, largest first; the names are not kept.</summary>
    private static List<int> TeamSizes(JsonElement root)
    {
        List<int> sizes = [];
        if (!root.TryGetProperty("teams", out JsonElement teams) || teams.ValueKind != JsonValueKind.Object)
        {
            return sizes;
        }

        foreach (JsonProperty team in teams.EnumerateObject())
        {
            int size = team.Value.ValueKind == JsonValueKind.Array
                ? team.Value.EnumerateArray().Count(name => name.ValueKind == JsonValueKind.String)
                : 0;

            // Team 1 means no team: each of its players stands alone.
            if (team.Name == "1")
            {
                sizes.AddRange(Enumerable.Repeat(1, size));
            }
            else if (size > 0)
            {
                sizes.Add(size);
            }
        }

        sizes.Sort((a, b) => b.CompareTo(a));
        return sizes;
    }

    /// <summary>
    /// A readable map name from the vault folder ("setons_clutch.v0003" becomes "Setons Clutch"); the
    /// real display name is in the replay's scenario, inside the compressed body, which a preview does
    /// not download. The original maps are codes ("scmp_009", "x1mp_017"), so they get no name.
    /// </summary>
    internal static string? MapDisplayName(string? folder)
    {
        if (folder is not { Length: > 0 })
        {
            return null;
        }

        int version = folder.LastIndexOf(".v", StringComparison.OrdinalIgnoreCase);
        string name = version > 0 && folder[(version + 2)..].All(char.IsAsciiDigit) ? folder[..version] : folder;
        if (IsOriginalMapCode(name))
        {
            return null;
        }

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.Replace('_', ' ').Trim());
    }

    private static bool IsOriginalMapCode(string name)
    {
        int separator = name.IndexOf('_');
        return separator > 0
            && name[..separator].ToLowerInvariant() is "scmp" or "x1mp"
            && name[(separator + 1)..].All(char.IsAsciiDigit);
    }

    /// <summary>The description of a card, e.g. "#25717491 · 1v1 · 7 Oct 2025 · 1h 34m".</summary>
    internal static string Summary(ReplayCard card)
    {
        // Without a map name the title is the replay id already.
        List<string> parts = card.MapName is null ? [] : [$"#{card.ReplayId}"];
        if (TeamLayout(card.TeamSizes) is { } layout)
        {
            parts.Add(layout);
        }
        else if (card.PlayerCount > 0)
        {
            parts.Add(card.PlayerCount == 1 ? "1 player" : $"{card.PlayerCount} players");
        }
        if (card.LaunchedAt is { } launchedAt)
        {
            parts.Add(launchedAt.ToString("d MMM yyyy", CultureInfo.InvariantCulture));
        }
        if (card.Duration is { } duration)
        {
            parts.Add(duration.TotalHours >= 1 ? $"{(int)duration.TotalHours}h {duration.Minutes}m" : $"{Math.Max(1, duration.Minutes)}m");
        }

        return string.Join(" · ", parts);
    }

    /// <summary>
    /// "1v1", "4v4", "3v2", "1v1v1"; a free-for-all of more than four players is "6-player FFA".
    /// Null for a game of one team (or none recorded), where a layout says nothing.
    /// </summary>
    internal static string? TeamLayout(IReadOnlyList<int> sizes)
    {
        if (sizes.Count < 2)
        {
            return null;
        }

        return sizes.Count > 4 && sizes.All(size => size == 1)
            ? $"{sizes.Count}-player FFA"
            : string.Join("v", sizes);
    }

    private string RenderTags(ReplayCard card, string pageUrl)
    {
        // The map names the game best. The lobby title is not used: it is often just the players'
        // names ("Team A Vs Team B"), or nothing readable at all.
        string title = card.MapName ?? $"Replay #{card.ReplayId}";
        string description = Summary(card);

        StringBuilder tags = new();
        tags.AppendLine($"{BlockStart} (Open Graph) for replay #{card.ReplayId}, filled in by FAForever.Vault.Server. -->");
        Meta(tags, "og:type", "website");
        Meta(tags, "og:site_name", "Vault of FAF");
        Meta(tags, "og:title", title);
        Meta(tags, "og:description", description);
        Meta(tags, "og:url", pageUrl);
        if (card.MapFolder is { Length: > 0 } folder)
        {
            Meta(tags, "og:image", string.Format(CultureInfo.InvariantCulture, MapPreviewUrlFormat, Uri.EscapeDataString(folder.ToLowerInvariant())));
            Meta(tags, "og:image:alt", card.MapName is { } mapName ? $"Map preview of {mapName}" : "Map preview");
            Meta(tags, "twitter:card", "summary_large_image", name: true);
        }
        else
        {
            Meta(tags, "twitter:card", "summary", name: true);
        }
        tags.Append("    ").Append(BlockEnd);
        return tags.ToString();
    }

    private static void Meta(StringBuilder tags, string property, string content, bool name = false)
        => tags.Append("    <meta ").Append(name ? "name" : "property").Append("=\"").Append(property)
            .Append("\" content=\"").Append(EncodeAttribute(content)).AppendLine("\" />");

    /// <summary>
    /// Escapes a double-quoted attribute value. Only what HTML requires, so names and the separator
    /// stay readable in the source (HtmlEncoder turns "·" and "+" into character references).
    /// </summary>
    private static string EncodeAttribute(string value) => value
        .Replace("&", "&amp;")
        .Replace("\"", "&quot;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\n", "&#10;");

    /// <summary>Replaces the default link preview block of index.html; the page as is when it has none.</summary>
    private static string ReplaceBlock(string html, string block)
    {
        int start = html.IndexOf(BlockStart, StringComparison.Ordinal);
        int end = start < 0 ? -1 : html.IndexOf(BlockEnd, start, StringComparison.Ordinal);
        return end < 0 ? html : string.Concat(html.AsSpan(0, start), block, html.AsSpan(end + BlockEnd.Length));
    }

    private static string? String(JsonElement element, string name)
        => element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static double Number(JsonElement element, string name)
        => element.TryGetProperty(name, out JsonElement value) && value.TryGetDouble(out double number) ? number : 0;
}

/// <summary>What a link preview shows of a replay, from its metadata.</summary>
public sealed record ReplayCard(
    int ReplayId,
    string? MapFolder,
    string? MapName,
    int PlayerCount,
    List<int> TeamSizes,
    DateTimeOffset? LaunchedAt,
    TimeSpan? Duration);
