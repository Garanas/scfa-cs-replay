using System.Net.Http.Headers;
using System.Numerics;
using System.Text;
using FAForever.FileFormats.Map;
using FAForever.Vault.Viewer.Services.Replays;

namespace FAForever.Vault.Viewer.Services.Maps;

/// <summary>
/// Reads a vault map in the browser. The content server serves map archives anonymously, with CORS
/// and range requests, so only the end of the archive (its directory) and the three files a map is
/// made of are downloaded: a map folder often holds megabytes of textures besides them (Theta Passage:
/// a 10.6 MB archive, 1.4 MB of map files). A suffix range (<c>bytes=-n</c>) is not a CORS-safelisted
/// header and fails, so the size comes from a HEAD request first.
/// </summary>
/// <remarks>
/// The work after the download (parsing the Lua files, generating the navigational mesh, measuring
/// distances) runs on the single WebAssembly thread; the loader yields to the browser between the
/// steps so the progress bar moves. The last few maps stay in memory while the app runs.
/// </remarks>
public sealed class MapLoader(HttpClient http)
{
    private const string ArchiveUrl = "https://content.faforever.com/maps/";
    private const int CacheSize = 3;

    private readonly List<LoadedMap> cache = [];

    /// <summary>
    /// The folder as the vault stores it: lower case. The content server is case sensitive and every
    /// folder in the vault is lower case (checked 2026-10-07: <c>HardFFA.v0001.zip</c> is a 404).
    /// </summary>
    public static string NormalizeFolder(string folder) => folder.Trim().ToLowerInvariant();

    public LoadedMap? Cached(string folder)
        => cache.FirstOrDefault(map => map.Folder == NormalizeFolder(folder));

    public async Task<LoadedMap> LoadAsync(string folder, IProgress<ReplayLoadProgress>? progress, CancellationToken cancellationToken)
    {
        folder = NormalizeFolder(folder);
        if (Cached(folder) is { } cached)
        {
            return cached;
        }

        string url = ArchiveUrl + Uri.EscapeDataString(folder) + ".zip";

        progress?.Report(new ReplayLoadProgress("Reading the map archive", null));
        long length = await GetLengthAsync(url, cancellationToken);
        long tailStart = Math.Max(0, length - MapArchive.TailLength);
        byte[] tail = await GetRangeAsync(url, tailStart, length - 1, cancellationToken);
        (long directoryOffset, long directoryLength) = MapArchive.FindDirectory(tail, tailStart);
        byte[] directory = directoryOffset >= tailStart
            ? tail.AsSpan((int)(directoryOffset - tailStart), (int)directoryLength).ToArray()
            : await GetRangeAsync(url, directoryOffset, directoryOffset + directoryLength - 1, cancellationToken);
        IReadOnlyList<MapArchiveEntry> entries = MapArchive.ReadDirectory(directory);

        MapArchiveEntry scenarioEntry = MapArchive.FindScenario(entries)
            ?? throw new FormatException("The map archive holds no scenario (_scenario.lua)");
        MapScenario scenario = MapScenarioParser.Parse(Encoding.UTF8.GetString(await ReadEntryAsync(url, scenarioEntry, cancellationToken)));

        MapArchiveEntry scmapEntry = (scenario.Map is { } mapPath ? MapArchive.FindByGamePath(entries, mapPath, scenarioEntry) : null)
            ?? throw new FormatException("The map archive holds no .scmap for its scenario");
        MapArchiveEntry saveEntry = (scenario.Save is { } savePath ? MapArchive.FindByGamePath(entries, savePath, scenarioEntry) : null)
            ?? throw new FormatException("The map archive holds no _save.lua for its scenario");

        progress?.Report(new ReplayLoadProgress("Downloading the map", 20));
        Task<byte[]> scmapBytes = ReadEntryAsync(url, scmapEntry, cancellationToken);
        Task<byte[]> saveBytes = ReadEntryAsync(url, saveEntry, cancellationToken);
        await Task.WhenAll(scmapBytes, saveBytes);

        progress?.Report(new ReplayLoadProgress("Reading the markers and units", 40));
        await Task.Delay(1, cancellationToken);
        MapSave save = MapSaveParser.Parse(Encoding.UTF8.GetString(saveBytes.Result));

        progress?.Report(new ReplayLoadProgress("Reading the terrain", 55));
        await Task.Delay(1, cancellationToken);
        Scmap scmap = ScmapParser.Parse(scmapBytes.Result);

        progress?.Report(new ReplayLoadProgress("Working out where units can go", 65));
        await Task.Delay(1, cancellationToken);
        MapArea? playableArea = NavGenerator.FindPlayableArea(scenario, save);
        MapNavigation navigation = NavGenerator.Generate(scmap, save, playableArea);

        progress?.Report(new ReplayLoadProgress("Measuring the routes", 85));
        await Task.Delay(1, cancellationToken);
        List<MapMarker> starts = [.. scenario.PlayerArmies.Select(army => save.GetMarker(army)).OfType<MapMarker>()];
        List<MapMarker> mass = [.. save.GetMarkers("Mass")];
        ExtractorDistances extractors = ExtractorLayout.Measure(navigation, starts, mass);
        (int, int)? closest = ClosestStarts(extractors);

        List<Vector3> fairness = [.. starts.Select(start => start.Position), .. save.GetMarkers("Mass").Select(marker => marker.Position), .. save.GetMarkers("Hydrocarbon").Select(marker => marker.Position)];
        (double underWater, double steep) = TerrainShares(scmap);

        LoadedMap loaded = new(folder, scenario, save, scmap, navigation, playableArea, starts, extractors, MapSymmetry.Find(scmap.Width, scmap.Height, fairness))
        {
            ClosestStarts = closest,
            UnderWater = underWater,
            Steep = steep,
        };

        // the overview shows the route of the layer that links the start positions; the others wait for their tab
        await Task.Delay(1, cancellationToken);
        _ = loaded.ConnectingRoute;

        cache.Insert(0, loaded);
        if (cache.Count > CacheSize)
        {
            cache.RemoveAt(cache.Count - 1);
        }
        return loaded;
    }

    /// <summary>The pair of start positions closest to each other along the routes.</summary>
    private static (int, int)? ClosestStarts(ExtractorDistances distances)
    {
        (int, int)? best = null;
        float bestDistance = float.PositiveInfinity;
        for (int a = 0; a < distances.NearestToStarts.Count; a++)
        {
            NearestOrigins nearest = distances.NearestToStarts[a];
            if (nearest.Second >= 0 && nearest.SecondDistance < bestDistance)
            {
                bestDistance = nearest.SecondDistance;
                best = (Math.Min(a, nearest.Second), Math.Max(a, nearest.Second));
            }
        }
        return best;
    }

    /// <summary>The share of ogrids below the water surface and the share with a side steeper than land units climb.</summary>
    private static (double UnderWater, double Steep) TerrainShares(Scmap scmap)
    {
        ScmapHeightmap heights = scmap.Heightmap;
        float water = scmap.Water.HasWater ? scmap.Water.Elevation : float.NegativeInfinity;
        int under = 0;
        int steep = 0;
        for (int z = 0; z < scmap.Height; z++)
        {
            for (int x = 0; x < scmap.Width; x++)
            {
                float h = heights.GetHeight(x, z);
                if (h < water)
                {
                    under++;
                }
                if (MathF.Abs(heights.GetHeight(x + 1, z) - h) >= NavGenerator.MaxHeightDifference
                    || MathF.Abs(heights.GetHeight(x, z + 1) - h) >= NavGenerator.MaxHeightDifference)
                {
                    steep++;
                }
            }
        }
        double cells = (double)scmap.Width * scmap.Height;
        return (under / cells, steep / cells);
    }

    private async Task<long> GetLengthAsync(string url, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Head, url);
        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new MapNotFoundException();
        }
        response.EnsureSuccessStatusCode();
        return response.Content.Headers.ContentLength ?? throw new InvalidOperationException("The content server did not say how large the map archive is");
    }

    private async Task<byte[]> GetRangeAsync(string url, long from, long to, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, url);
        request.Headers.Range = new RangeHeaderValue(from, to);
        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        // a server that ignores the range sends the whole archive
        return response.StatusCode == System.Net.HttpStatusCode.PartialContent || bytes.Length == to - from + 1
            ? bytes
            : bytes.AsSpan((int)from, (int)Math.Min(to - from + 1, bytes.Length - from)).ToArray();
    }

    private async Task<byte[]> ReadEntryAsync(string url, MapArchiveEntry entry, CancellationToken cancellationToken)
    {
        byte[] local = await GetRangeAsync(url, entry.LocalHeaderOffset, entry.LocalHeaderOffset + entry.SpanLength() - 1, cancellationToken);
        return MapArchive.ReadEntry(local, entry);
    }
}

/// <summary>Thrown when the vault has no archive for a map folder.</summary>
public sealed class MapNotFoundException : Exception;
