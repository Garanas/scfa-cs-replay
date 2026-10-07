using System.Net;
using System.Net.Http.Headers;
using FAForever.FileFormats.Replay;

namespace FAForever.Vault.Viewer.Services.Replays;

/// <summary>
/// The map folder of a vault game, read from the start of its replay file, for games the FAF API
/// links no map to (generated maps). Only the start is downloaded: a Range request through the
/// API's redirect, which the content server answers anonymously with CORS. Asked for on a click,
/// never for a whole page of results; answers are kept while the app runs.
/// </summary>
public sealed class ReplayMapLookup(HttpClient http, IConfiguration configuration)
{
    /// <summary>
    /// Enough for most replays; the rest need the first zstd block in full, which the second size
    /// always holds (see <see cref="ReplayLoader.TryReadPathToScenario"/>).
    /// </summary>
    private static readonly int[] StartSizes = [64 * 1024, 256 * 1024];

    private readonly Dictionary<int, Task<string?>> lookups = [];

    /// <summary>The map folder of the game ("osiris.v0006", "neroxis_map_generator_…"), or null when the replay does not say.</summary>
    public Task<string?> FolderAsync(int gameId)
    {
        if (!lookups.TryGetValue(gameId, out Task<string?>? lookup) || lookup.IsFaulted || lookup.IsCanceled)
        {
            lookup = LookUpAsync(gameId);
            lookups[gameId] = lookup;
        }

        return lookup;
    }

    /// <summary>Whether the game was looked up before, so a card shows the answer again without a click.</summary>
    public bool TryGetKnown(int gameId, out string? folder)
    {
        folder = null;
        if (lookups.TryGetValue(gameId, out Task<string?>? lookup) && lookup.IsCompletedSuccessfully)
        {
            folder = lookup.Result;
            return true;
        }

        return false;
    }

    private async Task<string?> LookUpAsync(int gameId)
    {
        string apiBase = configuration["FafApi:BaseUrl"] ?? "https://api.faforever.com";
        foreach (int size in StartSizes)
        {
            using HttpRequestMessage request = new(HttpMethod.Get, $"{apiBase}/game/{gameId}/replay");
            request.Headers.Range = new RangeHeaderValue(0, size - 1);

            using HttpResponseMessage response = await http.SendAsync(request);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            response.EnsureSuccessStatusCode();

            byte[] start = await response.Content.ReadAsByteArrayAsync();
            if (MapPreviews.FolderOfPath(ReplayLoader.TryReadPathToScenario(start)) is { } folder)
            {
                return folder;
            }

            // the whole file came back (a server without ranges, or a short file): reading more cannot help
            if (start.Length < size)
            {
                return null;
            }
        }

        return null;
    }
}
