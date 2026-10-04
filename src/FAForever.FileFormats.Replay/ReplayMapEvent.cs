
namespace FAForever.FileFormats.Replay
{
    /// <summary>
    /// How a positional event entered the replay stream.
    /// </summary>
    public enum ReplayMapEventKind
    {
        /// <summary>An IssueCommand with a position target (move, attack-move, build, ...).</summary>
        Command,

        /// <summary>An IssueFactoryCommand with a position target (e.g. a rally point).</summary>
        FactoryCommand,

        /// <summary>An UpdateCommandTarget that moved a queued command to a new position.</summary>
        Retarget,

        /// <summary>A CreateUnit console command (cheats / sandbox games).</summary>
        UnitSpawn,
    }

    /// <summary>
    /// A player intent with a world position, for playing a replay back on the map. The
    /// top-left corner of the map is (0, 0); X runs to the right and Z runs down (see
    /// <see cref="ReplayAnalysis.MapPosition"/>). Commands only carry their clicked target
    /// position - the ordered units themselves are not parsed, so the origin is unknown.
    /// </summary>
    public record ReplayMapEvent(
        TimeSpan Timestamp,
        int SourceId,
        ReplayMapEventKind Kind,
        CommandType CommandType,
        float X,
        float Z,
        string? BlueprintId,
        int UnitCount);
}
