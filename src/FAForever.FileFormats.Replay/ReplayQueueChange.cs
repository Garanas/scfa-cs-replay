namespace FAForever.FileFormats.Replay
{
    /// <summary>
    /// A change to the count of a queued order, e.g. clicking a unit in the factory build menu
    /// again to queue one more (IncreaseBuildCountInQueue / DecreaseBuildCountInQueue). The
    /// order is resolved from the command identifier, so CommandType and BlueprintId are those
    /// of the order whose count changed; both are null when the order is not in the replay.
    /// Delta is signed: positive adds to the queue, negative removes from it. A decrease with
    /// a delta of 0 is recorded as 0.
    /// </summary>
    public record ReplayQueueChange(
        TimeSpan Timestamp,
        int SourceId,
        int Delta,
        CommandType? CommandType,
        string? BlueprintId);
}
