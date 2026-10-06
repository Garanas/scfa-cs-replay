namespace FAForever.Vault.Viewer.Features.Search;

/// <summary>The addresses of the replay pages, base relative (AGENTS.md: in-app links have no leading slash).</summary>
public static class ReplayLinks
{
    /// <summary>The entry page, with a card for each way to a replay.</summary>
    public const string Landing = "replays";

    /// <summary>The search of the FAF vault (needs a login).</summary>
    public const string Search = "replays/search";

    /// <summary>The replays in a folder on this computer (installed app only).</summary>
    public const string Folder = "replays/folder";

    /// <summary>A replay file from this computer, held in memory; without one, a file picker.</summary>
    public const string Local = "replays/local";

    /// <summary>A replay of the FAF vault: the address to share.</summary>
    public static string Vault(int gameId) => $"replays/{gameId}";
}
