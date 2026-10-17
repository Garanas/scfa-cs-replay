using FAForever.Vault.Viewer.Services.Players;

namespace FAForever.Vault.Viewer.Features.Players.Matches;

/// <summary>
/// The one short line the Players column of the Matches tab shows for a game, and the lists of the
/// hover card behind it. The signed in player's own row is never part of it.
/// </summary>
public sealed record MatchPlayersLine(
    string Text,
    MatchParticipant? Opponent,
    IReadOnlyList<MatchParticipant> Mates,
    IReadOnlyList<MatchParticipant> Opponents)
{
    /// <summary>The line of a game for the player: a 1v1 names the opponent, a team game counts both sides.</summary>
    public static MatchPlayersLine Of(PlayerMatch match, int playerId)
    {
        (IReadOnlyList<MatchParticipant> mates, IReadOnlyList<MatchParticipant> opponents) = MatchFormat.Others(match, playerId);
        int total = mates.Count + opponents.Count;

        if (total == 0)
        {
            return new MatchPlayersLine("No other players", null, mates, opponents);
        }

        if (mates.Count == 0 && opponents.Count == 1)
        {
            return new MatchPlayersLine("vs " + opponents[0].Login, opponents[0], mates, opponents);
        }

        if (mates.Count == 0)
        {
            return new MatchPlayersLine($"{opponents.Count} others", null, mates, opponents);
        }

        string with = $"{mates.Count} with";
        string text = opponents.Count == 0 ? with : $"{with} · {opponents.Count} against";
        return new MatchPlayersLine(text, null, mates, opponents);
    }
}
