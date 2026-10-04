
namespace FAForever.FileFormats.Replay
{
    /// <summary>
    /// A coarse grouping of <see cref="CommandType"/> values for display purposes, such as
    /// picking a marker icon. The grouping is game knowledge and therefore lives here, not
    /// in a front-end.
    /// </summary>
    public enum CommandCategory
    {
        Move,
        Attack,
        Aggressive,
        Patrol,
        Build,
        Launch,
        Reclaim,
        Repair,
        Capture,
        Guard,
        Transport,
        Teleport,
        Stop,
        Special,
    }

    public static class CommandCategories
    {
        /// <summary>
        /// The display category of a command type. Exhaustive: unknown or future command
        /// types fall back to <see cref="CommandCategory.Special"/>.
        /// </summary>
        public static CommandCategory GetCategory(CommandType type) => type switch
        {
            CommandType.IssueMove => CommandCategory.Move,
            CommandType.IssueFormMove => CommandCategory.Move,
            CommandType.ASSIST_MOVE => CommandCategory.Move,
            CommandType.IssueDive => CommandCategory.Move,

            CommandType.IssueAttack => CommandCategory.Attack,
            CommandType.IssueFormAttack => CommandCategory.Attack,
            CommandType.IssueOvercharge => CommandCategory.Attack,

            CommandType.IssueAggressiveMove => CommandCategory.Aggressive,
            CommandType.IssueFormAggressiveMove => CommandCategory.Aggressive,

            CommandType.IssuePatrol => CommandCategory.Patrol,
            CommandType.IssueFormPatrol => CommandCategory.Patrol,
            CommandType.IssueFerry => CommandCategory.Patrol,

            CommandType.IssueBuildFactory => CommandCategory.Build,
            CommandType.IssueBuildMobile => CommandCategory.Build,
            CommandType.IssueUpgrade => CommandCategory.Build,

            CommandType.IssueNuke => CommandCategory.Launch,
            CommandType.IssueTactical => CommandCategory.Launch,
            CommandType.IssueSiloBuildTactical => CommandCategory.Launch,
            CommandType.IssueSiloBuildNuke => CommandCategory.Launch,

            CommandType.IssueReclaim => CommandCategory.Reclaim,
            CommandType.IssueSacrifice => CommandCategory.Reclaim,

            CommandType.IssueRepair => CommandCategory.Repair,
            CommandType.BuildAssist => CommandCategory.Repair,

            CommandType.IssueCapture => CommandCategory.Capture,

            CommandType.IssueGuard => CommandCategory.Guard,
            CommandType.ASSIST_COMMANDER => CommandCategory.Guard,

            CommandType.IssueTransportLoad => CommandCategory.Transport,
            CommandType.IssueTransportUnload => CommandCategory.Transport,
            CommandType.IssueTransportUnloadSpecific => CommandCategory.Transport,
            CommandType.TRANSPORT_REVERSE_LOAD_UNITS => CommandCategory.Transport,
            CommandType.DETACH_FROM_TRANSPORT => CommandCategory.Transport,
            CommandType.DOCK => CommandCategory.Transport,

            CommandType.IssueTeleport => CommandCategory.Teleport,

            CommandType.IssueStop => CommandCategory.Stop,
            CommandType.IssuePause => CommandCategory.Stop,
            CommandType.IssueKillSelf => CommandCategory.Stop,
            CommandType.IssueDestroySelf => CommandCategory.Stop,

            _ => CommandCategory.Special,
        };
    }
}
