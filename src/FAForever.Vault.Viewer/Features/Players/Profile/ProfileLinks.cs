namespace FAForever.Vault.Viewer.Features.Players.Profile;

/// <summary>Addresses inside a profile that carry the board and the period (defaults stay out of the address).</summary>
public static class ProfileLinks
{
    /// <summary>The Rating history tab of a player, on a board and a period; either may be null for the default.</summary>
    public static string RatingTab(int playerId, string? board = null, string? period = null, string? defaultBoard = null)
    {
        List<string> query = [$"{PlayerLinks.TabParameter}={PlayerLinks.TabRating}"];
        if (board is { Length: > 0 } && board != defaultBoard)
        {
            query.Add($"board={Uri.EscapeDataString(board)}");
        }

        if (period is { Length: > 0 } && period != PlayerFilters.DefaultPeriod)
        {
            query.Add($"period={Uri.EscapeDataString(period)}");
        }

        return $"{PlayerLinks.Profile(playerId)}?{string.Join('&', query)}";
    }

    /// <summary>The replay search for a player's name: every replay they played.</summary>
    public static string AllReplays(string playerName) => $"{FAForever.Vault.Viewer.Features.Search.ReplayLinks.Search}?player={Uri.EscapeDataString(playerName)}";
}
