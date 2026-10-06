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
}
