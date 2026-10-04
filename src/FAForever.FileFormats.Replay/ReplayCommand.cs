namespace FAForever.FileFormats.Replay
{
    /// <summary>
    /// An order a player gave: an IssueCommand to selected units (factories included: queueing
    /// an engineer is an IssueBuildFactory command without a position), or an IssueFactoryCommand
    /// (<see cref="FromFactory"/>), the orders factories pass on to what they build, such as
    /// rally points. Position is the clicked target, or null when the order has none (factory
    /// queues, unit targets, stop). The coordinates follow
    /// <see cref="ReplayAnalysis.MapPosition"/>: (0, 0) is the top-left corner, Z runs down.
    /// </summary>
    public record ReplayCommand(
        TimeSpan Timestamp,
        int SourceId,
        bool FromFactory,
        CommandType CommandType,
        ReplayAnalysis.MapPosition? Position,
        string? BlueprintId,
        int UnitCount)
    {
        /// <summary>
        /// The commander upgrade an IssueScript order starts, e.g. "AdvancedEngineering"
        /// (its Lua parameters are {TaskName='EnhanceTask', Enhancement=...}); a name ending in
        /// "Remove" takes one off. Null for every other order.
        /// </summary>
        public string? Enhancement { get; init; }
    }
}
