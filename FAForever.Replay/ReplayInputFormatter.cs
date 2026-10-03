using System.Globalization;

namespace FAForever.Replay
{
    /// <summary>
    /// Renders a <see cref="ReplayInput"/> as a short, human-readable, single-line
    /// description, e.g. "Move → (512, 300) · 5 units" or "Build uab0101 (queued)".
    /// Complements <see cref="LuaDataFormatter"/>, which handles the Lua payloads.
    /// </summary>
    public static class ReplayInputFormatter
    {
        public static string Describe(ReplayInput input)
        {
            return input switch
            {
                ReplayInput.IssueCommand command => DescribeCommand(command.Data, command.Units, "units"),
                ReplayInput.IssueFactoryCommand factory => "Factory: " + DescribeCommand(factory.Data, factory.Factories, "factories"),
                ReplayInput.IncreaseCommandCount increase => Invariant($"Queue +{increase.Delta} (command #{increase.CommandId})"),
                ReplayInput.DecreaseCommandCount decrease => Invariant($"Queue -{decrease.Delta} (command #{decrease.CommandId})"),
                ReplayInput.UpdateCommandTarget update => Invariant($"Retarget command #{update.CommandId}{DescribeTarget(update.Target)}"),
                ReplayInput.UpdateCommandType update => Invariant($"Change command #{update.CommandId} to {Verb(update.Type)}"),
                ReplayInput.UpdateCommandLuaParameters update => Invariant($"Update command #{update.CommandId} parameters {LuaDataFormatter.Format(update.LuaParameters, maxDepth: 2)}"),
                ReplayInput.RemoveCommandFromQueue remove => Invariant($"Remove command #{remove.CommandId} from queue"),
                ReplayInput.SimCallback callback => DescribeCallback(callback),
                ReplayInput.RequestPause => "Paused the game",
                ReplayInput.RequestResume => "Resumed the game",
                ReplayInput.SingleStep => "Single simulation step",
                ReplayInput.CommandSourceTerminated => "Left the game",
                ReplayInput.EndGame => "Game ended",
                ReplayInput.CreateUnit create => Invariant($"Spawn {create.BlueprintId} at ({create.X:0}, {create.Z:0})"),
                ReplayInput.CreateProp create => Invariant($"Spawn prop {create.BlueprintId} at ({create.X:0}, {create.Z:0})"),
                ReplayInput.DestroyEntity destroy => Invariant($"Destroy entity #{destroy.EntityId}"),
                ReplayInput.WarpEntity warp => Invariant($"Warp entity #{warp.EntityId} to ({warp.X:0}, {warp.Z:0})"),
                ReplayInput.ProcessInfoPair info => Invariant($"Info {info.Name} = {info.Value}"),
                ReplayInput.ExecuteLuaInSim lua => "Lua: " + Truncate(lua.LuaCode, 80),
                ReplayInput.DebugCommand debug => Invariant($"Debug: {debug.Command}"),
                _ => input.GetType().Name,
            };
        }

        /// <summary>
        /// The blueprint the input refers to, when it refers to one - for showing unit icons.
        /// </summary>
        public static string? TryGetBlueprintId(ReplayInput input)
        {
            string? blueprintId = input switch
            {
                ReplayInput.IssueCommand command => command.Data.BlueprintId,
                ReplayInput.IssueFactoryCommand factory => factory.Data.BlueprintId,
                ReplayInput.CreateUnit create => create.BlueprintId,
                ReplayInput.CreateProp create => create.BlueprintId,
                _ => null,
            };

            return string.IsNullOrEmpty(blueprintId) ? null : blueprintId;
        }

        private static string DescribeCommand(CommandData data, CommandUnits units, string unitsNoun)
        {
            string blueprint = string.IsNullOrEmpty(data.BlueprintId) ? "" : " " + data.BlueprintId;
            string target = DescribeTarget(data.Target);
            string count = units.UnitCount > 0 ? Invariant($" · {units.UnitCount} {unitsNoun}") : "";
            string queued = data.ClearQueue ? "" : " (queued)";
            return $"{Verb(data.Type)}{blueprint}{target}{count}{queued}";
        }

        private static string DescribeCallback(ReplayInput.SimCallback callback)
        {
            string parameters = callback.LuaParameters is LuaData.Nil ? "" : " " + LuaDataFormatter.Format(callback.LuaParameters, maxDepth: 2);
            string count = callback.Units.UnitCount > 0 ? Invariant($" · {callback.Units.UnitCount} units") : "";
            return $"Callback {callback.Endpoint}{parameters}{count}";
        }

        private static string DescribeTarget(CommandTarget target) => target switch
        {
            CommandTarget.Position position => Invariant($" → ({position.X:0}, {position.Z:0})"),
            CommandTarget.Entity entity => Invariant($" → entity #{entity.EntityId}"),
            _ => "",
        };

        /// <summary>
        /// The human-readable verb of a command type, e.g. "Move" or "Launch nuke".
        /// </summary>
        public static string Verb(CommandType type) => type switch
        {
            CommandType.None => "Command",
            CommandType.IssueStop => "Stop",
            CommandType.IssueMove => "Move",
            CommandType.IssueDive => "Dive",
            CommandType.IssueFormMove => "Form move",
            CommandType.IssueSiloBuildTactical => "Build tactical missile",
            CommandType.IssueSiloBuildNuke => "Build nuke",
            CommandType.IssueBuildFactory => "Build",
            CommandType.IssueBuildMobile => "Build",
            CommandType.BuildAssist => "Assist build",
            CommandType.IssueAttack => "Attack",
            CommandType.IssueFormAttack => "Form attack",
            CommandType.IssueNuke => "Launch nuke",
            CommandType.IssueTactical => "Launch tactical missile",
            CommandType.IssueTeleport => "Teleport",
            CommandType.IssueGuard => "Guard / assist",
            CommandType.IssuePatrol => "Patrol",
            CommandType.IssueFerry => "Ferry",
            CommandType.IssueFormPatrol => "Form patrol",
            CommandType.IssueReclaim => "Reclaim",
            CommandType.IssueRepair => "Repair",
            CommandType.IssueCapture => "Capture",
            CommandType.IssueTransportLoad => "Load transport",
            CommandType.TRANSPORT_REVERSE_LOAD_UNITS => "Reverse load transport",
            CommandType.IssueTransportUnload => "Unload transport",
            CommandType.IssueTransportUnloadSpecific => "Unload specific units",
            CommandType.DETACH_FROM_TRANSPORT => "Detach from transport",
            CommandType.IssueUpgrade => "Upgrade",
            CommandType.IssueScript => "Script",
            CommandType.ASSIST_COMMANDER => "Assist commander",
            CommandType.IssueKillSelf => "Self-destruct",
            CommandType.IssueDestroySelf => "Destroy self",
            CommandType.IssueSacrifice => "Sacrifice",
            CommandType.IssuePause => "Pause unit",
            CommandType.IssueOvercharge => "Overcharge",
            CommandType.IssueAggressiveMove => "Aggressive move",
            CommandType.IssueFormAggressiveMove => "Form aggressive move",
            CommandType.ASSIST_MOVE => "Assist move",
            CommandType.SPECIAL_ACTION => "Special action",
            CommandType.DOCK => "Dock",
            _ => type.ToString(),
        };

        private static string Truncate(string text, int maxLength)
            => text.Length > maxLength ? text[..maxLength] + "…" : text;

        private static string Invariant(FormattableString text)
            => text.ToString(CultureInfo.InvariantCulture);
    }
}
