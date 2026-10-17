using FAForever.Vault.Viewer.Features.Maps;
using FAForever.Vault.Viewer.Features.Players;
using FAForever.Vault.Viewer.Features.Search;

namespace FAForever.Vault.Viewer.Services.Auth;

/// <summary>The addresses the account menu links to, base relative, for the signed in player.</summary>
public static class AccountLinks
{
    /// <summary>The player's own stats; null when the session's id is not a number.</summary>
    public static string? Profile(string? userId) => int.TryParse(userId, out var id) ? PlayerLinks.Profile(id) : null;

    /// <summary>The replay search for games the player took part in.</summary>
    public static string Replays(string name) => $"{ReplayLinks.Search}?player={Uri.EscapeDataString(name)}";

    /// <summary>The map search for maps the player made.</summary>
    public static string Maps(string name) => $"{MapLinks.Search}?author={Uri.EscapeDataString(name)}";
}
