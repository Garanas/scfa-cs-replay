namespace FAForever.Vault.Viewer.Features.Maps;

/// <summary>The addresses of the map pages, base relative (AGENTS.md: in-app links have no leading slash).</summary>
public static class MapLinks
{
    /// <summary>The entry page, with a card for each page below.</summary>
    public const string Landing = "maps";

    /// <summary>The featured maps, chosen by the FAF team (<c>recommended</c> in the API).</summary>
    public const string Featured = "maps/featured";

    /// <summary>The current map pools of the matchmaker.</summary>
    public const string Ladder = "maps/ladder";

    /// <summary>Every visible map, with filters.</summary>
    public const string Search = "maps/search";

    /// <summary>
    /// The page of one map version, by its vault folder (<c>maps/theta_passage_-_faf_version.v0001</c>).
    /// It reads the map's own files, so it works signed out.
    /// </summary>
    public static string Map(string folder) => "maps/" + Uri.EscapeDataString(folder.ToLowerInvariant());

    /// <summary>
    /// The replay search for games on this map, played in a month around today. The window is not
    /// optional: without one a popular map takes 14 s (DualGap Adaptive, 889,230 games), with it
    /// 0.3 s (measured 2026-10-06, FAForever/faf-java-api#1182). The search page can widen it.
    /// </summary>
    public static string Replays(string mapName) => Replays(mapName, DateOnly.FromDateTime(DateTime.Today));

    /// <summary>The replay search on this map in a month around a day, e.g. the newest game on it.</summary>
    public static string Replays(string mapName, DateOnly around) =>
        $"replays/search?map={Uri.EscapeDataString(mapName)}&around={around:yyyy-MM-dd}&within=month";
}
