
namespace FAForever.Replay
{
    /// <summary>
    /// The kind of a <see cref="ReplaySessionEvent"/>.
    /// </summary>
    public enum ReplaySessionEventKind
    {
        /// <summary>A player paused the game (RequestPause).</summary>
        Paused,

        /// <summary>A player resumed the game (RequestResume).</summary>
        Resumed,

        /// <summary>A command source stopped producing input: the player left or finished (CommandSourceTerminated).</summary>
        PlayerLeft,

        /// <summary>The game ended (EndGame).</summary>
        GameEnded,

        /// <summary>A player self-destructed units (IssueKillSelf / IssueDestroySelf).</summary>
        SelfDestruct,
    }

    /// <summary>
    /// A notable moment in the flow of the game session itself, as opposed to orders on the
    /// map: pauses, players leaving, self-destructs and the end of the game.
    /// </summary>
    public record ReplaySessionEvent(
        TimeSpan Timestamp,
        int SourceId,
        ReplaySessionEventKind Kind,
        int UnitCount = 0);
}
