
namespace FAForever.Replay
{
    /// <summary>
    /// An enumeration that maps 1-to-1 with the byte that represents the command type of a command in the binary.
    /// </summary>
    public enum CommandType
    {
        None = 0,
        IssueStop = 1,
        IssueMove = 2,
        IssueDive = 3,
        IssueFormMove = 4,
        IssueSiloBuildTactical = 5,
        IssueSiloBuildNuke = 6,
        IssueBuildFactory = 7,
        IssueBuildMobile = 8,
        BuildAssist = 9,
        IssueAttack = 10,
        IssueFormAttack = 11,
        IssueNuke = 12,
        IssueTactical = 13,
        IssueTeleport = 14,
        IssueGuard = 15,
        IssuePatrol = 16,
        IssueFerry = 17,
        IssueFormPatrol = 18,
        IssueReclaim = 19,
        IssueRepair = 20,
        IssueCapture = 21,
        IssueTransportLoad = 22,
        TRANSPORT_REVERSE_LOAD_UNITS = 23,
        IssueTransportUnload = 24,
        IssueTransportUnloadSpecific = 25,
        DETACH_FROM_TRANSPORT = 26,
        IssueUpgrade = 27,
        IssueScript = 28,
        ASSIST_COMMANDER = 29,
        IssueKillSelf = 30,
        IssueDestroySelf = 31,
        IssueSacrifice = 32,
        IssuePause = 33,
        IssueOvercharge = 34,
        IssueAggressiveMove = 35,
        IssueFormAggressiveMove = 36,
        ASSIST_MOVE = 37,
        SPECIAL_ACTION = 38,
        DOCK = 39,
    }
}
