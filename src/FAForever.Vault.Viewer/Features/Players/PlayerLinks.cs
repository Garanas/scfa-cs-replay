namespace FAForever.Vault.Viewer.Features.Players;

/// <summary>The sections of a player's profile; Overview is the default and stays out of the URL.</summary>
public enum PlayerTab
{
    Overview,
    Rating,
    Matches,
    Army,
}

/// <summary>The addresses of the Players pages, base relative (AGENTS.md: in-app links have no leading slash).</summary>
public static class PlayerLinks
{
    /// <summary>The entry page: a search for a player and cards to the pages below.</summary>
    public const string Landing = "players";

    /// <summary>The leaderboards, one board at a time.</summary>
    public const string Rankings = "players/rankings";

    /// <summary>The league standings, one season and subdivision at a time.</summary>
    public const string Leagues = "players/leagues";

    /// <summary>Two players side by side.</summary>
    public const string Compare = "players/compare";

    /// <summary>The query parameter that holds the profile's tab.</summary>
    public const string TabParameter = "tab";

    /// <summary>The tab values of the URL (Overview has none).</summary>
    public const string TabRating = "rating";

    public const string TabMatches = "matches";

    public const string TabArmy = "army";

    /// <summary>The profile of a player, by id (names change, ids do not): <c>players/2049311</c>.</summary>
    public static string Profile(int id) => $"players/{id}";

    /// <summary>The profile on one of its tabs: <c>players/2049311?tab=rating</c>; Overview adds nothing.</summary>
    public static string Profile(int id, PlayerTab tab) => TabToken(tab) is { } token ? $"{Profile(id)}?{TabParameter}={token}" : Profile(id);

    /// <summary>The landing page with a search text already in the field.</summary>
    public static string Search(string text) => $"{Landing}?q={Uri.EscapeDataString(text)}";

    /// <summary>The compare page for one or two players, by id.</summary>
    public static string CompareWith(int a, int? b = null) => b is { } second ? $"{Compare}?a={a}&b={second}" : $"{Compare}?a={a}";

    /// <summary>The URL token of a tab; null for Overview, which is the default.</summary>
    public static string? TabToken(PlayerTab tab) => tab switch
    {
        PlayerTab.Rating => TabRating,
        PlayerTab.Matches => TabMatches,
        PlayerTab.Army => TabArmy,
        _ => null,
    };

    /// <summary>The tab a URL value names; anything unknown is Overview.</summary>
    public static PlayerTab ParseTab(string? token) => token?.ToLowerInvariant() switch
    {
        TabRating => PlayerTab.Rating,
        TabMatches => PlayerTab.Matches,
        TabArmy => PlayerTab.Army,
        _ => PlayerTab.Overview,
    };

    public static string TabLabel(PlayerTab tab) => tab switch
    {
        PlayerTab.Rating => "Rating history",
        PlayerTab.Matches => "Matches",
        PlayerTab.Army => "Army and factions",
        _ => "Overview",
    };
}
