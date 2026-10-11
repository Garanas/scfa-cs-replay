
namespace FAForever.FileFormats.Replay
{
    /// <summary>
    /// A command source of the game: a player or an observer, in the order of
    /// <see cref="ReplayHeader.Clients"/> (players in army order, then observers).
    /// </summary>
    /// <param name="PlayerName">The name of the player.</param>
    /// <param name="PauseAllowance">How often the source may pause the game (<c>mTimeouts</c> in the
    /// engine): the lobby option Timeouts for players (3 on FAF), 0 for observers, who cannot pause,
    /// and -1 for unlimited (single player). A FAF player id is the army's <c>OwnerID</c> in
    /// <see cref="ReplayPlayerOptions.Raw"/>.</param>
    public record ReplaySource(String PlayerName, int PauseAllowance);
}
