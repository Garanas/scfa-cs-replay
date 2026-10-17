using System.Globalization;
using FAForever.Vault.Viewer.Features.Players.Matches;
using FAForever.Vault.Viewer.Services.Players;

namespace FAForever.Vault.Viewer.Features.Players.Leagues;

/// <summary>The pure parts of the league's "Latest games" card.</summary>
public static class LeagueGames
{
    /// <summary>What a load depends on: the players, the board, the season start and the page.</summary>
    public static string Key(IReadOnlyList<int> playerIds, string board, DateTimeOffset since, int page)
        => $"{string.Join(",", playerIds)}|{board}|{since.UtcTicks}|{page.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// The players of a game grouped by side: the lobby teams in order (a team of 2 and up), each
    /// player alone when nobody shares a team (team 1, or unknown).
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<MatchParticipant>> Sides(PlayerMatch game)
    {
        List<IReadOnlyList<MatchParticipant>> sides = [];
        foreach (IGrouping<int, MatchParticipant> team in game.Players.Where(player => player.Team is > 1).GroupBy(player => player.Team!.Value).OrderBy(group => group.Key))
        {
            sides.Add([.. team]);
        }

        foreach (MatchParticipant alone in game.Players.Where(player => player.Team is not > 1))
        {
            sides.Add([alone]);
        }

        return sides;
    }

    /// <summary>The result from the listed player's view: "Ashgrove won".</summary>
    public static string ResultText(PlayerMatch game)
    {
        string name = game.Players.FirstOrDefault(player => player.PlayerId == game.ListedPlayerId)?.Login ?? "";
        string word = game.Result switch
        {
            MatchResult.Victory => "won",
            MatchResult.Defeat => "lost",
            MatchResult.Draw => "drew",
            _ => "has no result",
        };
        return name.Length == 0 ? MatchFormat.ResultWord(game.Result) : $"{name} {word}";
    }

    /// <summary>The line under the table: the page and how many there are.</summary>
    public static string RangeText(int itemCount, int page, int? totalPages)
    {
        if (itemCount == 0)
        {
            return "No games to show";
        }

        string current = page.ToString("N0", CultureInfo.InvariantCulture);
        return totalPages is { } pages ? $"Page {current} of {pages.ToString("N0", CultureInfo.InvariantCulture)}" : $"Page {current}";
    }
}
