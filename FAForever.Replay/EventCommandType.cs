
namespace FAForever.Replay
{
    /// <summary>
    /// An enumeration that maps 1-to-1 with the byte that represents the command type of a command in the binary.
    /// </summary>
    public enum EventCommandType
    {
        // The order matters and should not be adjusted.

        None,
        IssueStop,
        IssueMove,
        IssueDive,
        IssueFormMove,
        IssueSiloBuildTactical,
        IssueSiloBuildNuke,
        IssueBuildFactory,
        IssueBuildMobile,
        BuildAssist,
        IssueAttack,
        IssueFormAttack,
        IssueNuke,
        IssueTactical,
        IssueTeleport,
        IssueGuard,
        IssuePatrol,
        IssueFerry,
        IssueFormPatrol,
        IssueReclaim,
        IssueRepair,
        IssueCapture,
        IssueTransportLoad,
        TRANSPORT_REVERSE_LOAD_UNITS,
        IssueTransportUnload,
        IssueTransportUnloadSpecific,
        DETACH_FROM_TRANSPORT,
        IssueUpgrade,
        IssueScript,
        ASSIST_COMMANDER,
        IssueKillSelf,
        IssueDestroySelf,
        IssueSacrifice,
        IssuePause,
        IssueOvercharge,
        IssueAggressiveMove,
        IssueFormAggressiveMove,
        ASSIST_MOVE,
        SPECIAL_ACTION,
        DOCK,
    }
}
