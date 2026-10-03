
namespace FAForever.Replay
{
    public static class ReplaySemantics
    {

        /// <summary>
        /// Retrieves all the chat messages from the replay input.
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public static List<ReplayChatMessage> GetChatMessages(Replay replay)
        {
            List<ReplayChatMessage> chatMessages = new List<ReplayChatMessage>();

            foreach (ReplayInput replayInput in replay.Body.UserInput)
            {
                switch (replayInput)
                {
                    case ReplayInput.SimCallback callback when callback.Endpoint == "GiveResourcesToPlayer" && callback.LuaParameters is LuaData.Table:

                        // Don't ask - this is how it works.
                        if (!(callback.LuaParameters is LuaData.Table luaTable) ||
                            !(luaTable.Value.TryGetValue("From", out LuaData? luaFrom) && luaFrom is LuaData.Number from) ||
                            !(luaTable.Value.TryGetValue("Sender", out LuaData? luaSender) && luaSender is LuaData.String sender) ||
                            !(luaTable.Value.TryGetValue("Msg", out LuaData? luaMsgTable) && luaMsgTable is LuaData.Table msgTable) ||
                            !(msgTable.Value.TryGetValue("to", out LuaData? luaTo) && luaTo is LuaData.String to) ||
                            !(msgTable.Value.TryGetValue("text", out LuaData? luaText) && luaText is LuaData.String text)
                        )
                        {
                            break;
                        }

                        // all players create a sim callback when one player sends a message. Requires refactoring in the game
                        if (sender.Value != replay.Header.Clients[replayInput.SourceId].PlayerName)
                        {
                            break;
                        }

                        chatMessages.Add(new ReplayChatMessage(TimeSpan.FromSeconds(replayInput.Tick / 10), sender.Value, to.Value, text.Value));

                        break;

                    default:
                        break;
                }
            }

            return chatMessages;
        }

        public static Dictionary<string, int> CountInputTypes(Replay replay)
        {

            Dictionary<string, int> inputTypes = new Dictionary<string, int>();

            foreach (ReplayInput replayInput in replay.Body.UserInput)
            {
                string key = "Unknown";
                switch (replayInput)
                {
                    case ReplayInput.CommandSourceTerminated:
                        key = "CommandSourceTerminated";
                        break;

                    case ReplayInput.CreateProp:
                        key = "CreateProp";
                        break;

                    case ReplayInput.CreateUnit:
                        key = "CreateUnit";
                        break;

                    case ReplayInput.DebugCommand:
                        key = "DebugCommand";
                        break;

                    case ReplayInput.DecreaseCommandCount:
                        key = "DecreaseCommandCount";
                        break;

                    case ReplayInput.DestroyEntity:
                        key = "DestroyEntity";
                        break;

                    case ReplayInput.EndGame:
                        key = "EndGame";
                        break;

                    case ReplayInput.Error:
                        key = "Error";
                        break;

                    case ReplayInput.ExecuteLuaInSim:
                        key = "ExecuteLuaInSim";
                        break;

                    case ReplayInput.IncreaseCommandCount:
                        key = "IncreaseCommandCount";
                        break;

                    case ReplayInput.IssueCommand:
                        key = "IssueCommand";
                        break;

                    case ReplayInput.IssueFactoryCommand:
                        key = "IssueFactoryCommand";
                        break;

                    case ReplayInput.ProcessInfoPair:
                        key = "ProcessInfoPair";
                        break;

                    case ReplayInput.RemoveCommandFromQueue:
                        key = "RemoveCommandFromQueue";
                        break;

                    case ReplayInput.RequestPause:
                        key = "RequestPause";
                        break;

                    case ReplayInput.RequestResume:
                        key = "RequestResume";
                        break;

                    case ReplayInput.SimCallback:
                        key = "SimCallback";
                        break;

                    case ReplayInput.SingleStep:
                        key = "SingleStep";
                        break;

                    case ReplayInput.Unknown:
                        key = "Unknown";
                        break;

                    case ReplayInput.UpdateCommandLuaParameters:
                        key = "UpdateCommandLuaParameters";
                        break;

                    case ReplayInput.UpdateCommandTarget:
                        key = "UpdateCommandTarget";
                        break;

                    case ReplayInput.UpdateCommandType:
                        key = "UpdateCommandType";
                        break;

                    case ReplayInput.WarpEntity:
                        key = "WarpEntity";
                        break;
                }

                if (!inputTypes.ContainsKey(key))
                {
                    inputTypes.Add(key, 0);
                }
                inputTypes[key]++;
            }

            return inputTypes;
        }

        /// <summary>
        /// Retrieves all resource transfers between players. Transfers share the sim callback
        /// of chat messages ("GiveResourcesToPlayer"); entries with a "Msg" table are chat and
        /// are skipped. The mass/energy values are fractions of the sender's current storage,
        /// not absolute amounts.
        /// </summary>
        public static List<ReplayResourceTransfer> GetResourceTransfers(Replay replay)
        {
            List<ReplayResourceTransfer> transfers = new List<ReplayResourceTransfer>();

            foreach (ReplayInput replayInput in replay.Body.UserInput)
            {
                if (replayInput is ReplayInput.SimCallback { Endpoint: "GiveResourcesToPlayer", LuaParameters: LuaData.Table table }
                    && !table.Value.ContainsKey("Msg")
                    && table.TryGetNumberValue("Mass", out double? mass)
                    && table.TryGetNumberValue("Energy", out double? energy)
                    && table.TryGetNumberValue("From", out double? fromArmy)
                    && table.TryGetNumberValue("To", out double? toArmy))
                {
                    transfers.Add(new ReplayResourceTransfer(
                        ReplayAnalysis.GetTimestamp(replayInput),
                        replayInput.SourceId,
                        (int)fromArmy!.Value,
                        (int)toArmy!.Value,
                        mass!.Value,
                        energy!.Value));
                }
            }

            return transfers;
        }

        /// <summary>
        /// Retrieves all pings the players placed on the map, from the SpawnPing and
        /// SpawnSpecialPing sim callbacks.
        /// </summary>
        public static List<ReplayPing> GetPings(Replay replay)
        {
            List<ReplayPing> pings = new List<ReplayPing>();

            foreach (ReplayInput replayInput in replay.Body.UserInput)
            {
                if (replayInput is ReplayInput.SimCallback { Endpoint: "SpawnPing" or "SpawnSpecialPing", LuaParameters: LuaData.Table table }
                    && table.TryGetStringValue("Type", out string? type) && type is not null
                    && table.TryGetTableValue("Location", out LuaData.Table? location) && location is not null
                    && location.TryGetNumberValue("1", out double? x) && x is { } locationX
                    && location.TryGetNumberValue("2", out double? y) && y is { } locationY
                    && location.TryGetNumberValue("3", out double? z) && z is { } locationZ)
                {
                    table.TryGetStringValue("Name", out string? name);
                    table.TryGetStringValue("Color", out string? color);
                    pings.Add(new ReplayPing(
                        ReplayAnalysis.GetTimestamp(replayInput),
                        replayInput.SourceId,
                        type,
                        (float)locationX,
                        (float)locationY,
                        (float)locationZ,
                        string.IsNullOrEmpty(name) ? null : name,
                        ToCssColor(color)));
                }
            }

            return pings;
        }

        /// <summary>
        /// Retrieves all brush strokes the players painted on the map, from the
        /// SharePaintingBrushStroke sim callback. Strokes with fewer than two samples are skipped.
        /// </summary>
        public static List<ReplayDrawing> GetDrawings(Replay replay) => GetDrawings(replay.Body.UserInput);

        /// <inheritdoc cref="GetDrawings(Replay)"/>
        public static List<ReplayDrawing> GetDrawings(IEnumerable<ReplayInput> inputs)
        {
            List<ReplayDrawing> drawings = new List<ReplayDrawing>();

            foreach (ReplayInput replayInput in inputs)
            {
                if (replayInput is ReplayInput.SimCallback { Endpoint: "SharePaintingBrushStroke", LuaParameters: LuaData.Table table }
                    && table.TryGetTableValue("ShareablePainting", out LuaData.Table? painting) && painting is not null
                    && painting.TryGetTableValue("Samples", out LuaData.Table? samples) && samples is not null)
                {
                    // The samples are interleaved x, y, z world coordinates; the map plane is (x, z).
                    List<ReplayAnalysis.MapPosition> points = new List<ReplayAnalysis.MapPosition>();
                    for (int i = 1;
                         samples.TryGetNumberValue(i.ToString(), out double? x) && x is { } sampleX
                         && samples.TryGetNumberValue((i + 2).ToString(), out double? z) && z is { } sampleZ;
                         i += 3)
                    {
                        points.Add(new ReplayAnalysis.MapPosition((float)sampleX, (float)sampleZ));
                    }

                    if (points.Count < 2)
                    {
                        continue;
                    }

                    painting.TryGetStringValue("PeerName", out string? peerName);
                    painting.TryGetNumberValue("ShareId", out double? shareId);
                    drawings.Add(new ReplayDrawing(
                        ReplayAnalysis.GetTimestamp(replayInput),
                        replayInput.SourceId,
                        string.IsNullOrEmpty(peerName) ? null : peerName,
                        shareId is { } id ? (int)id : null,
                        points));
                }
            }

            return drawings;
        }

        /// <summary>
        /// The clients that do not control an army: observers. They can chat, but they
        /// command nothing, so they have no actions, orders or build orders.
        /// </summary>
        public static List<ReplaySource> GetObservers(ReplayHeader header)
        {
            HashSet<int> sourcesWithArmy = new HashSet<int>();
            foreach (ReplayPlayerOptions army in header.Armies)
            {
                if (army.SourceId is { } sourceId)
                {
                    sourcesWithArmy.Add(sourceId);
                }
            }

            List<ReplaySource> observers = new List<ReplaySource>();
            for (int sourceId = 0; sourceId < header.Clients.Length; sourceId++)
            {
                if (!sourcesWithArmy.Contains(sourceId))
                {
                    observers.Add(header.Clients[sourceId]);
                }
            }

            return observers;
        }

        /// <summary>
        /// Retrieves the events the game logged for moderation via the ModeratorEvent sim
        /// callback, such as ping creations and self-destructs.
        /// </summary>
        public static List<ReplayModeratorEvent> GetModeratorEvents(Replay replay)
        {
            List<ReplayModeratorEvent> events = new List<ReplayModeratorEvent>();

            foreach (ReplayInput replayInput in replay.Body.UserInput)
            {
                if (replayInput is ReplayInput.SimCallback { Endpoint: "ModeratorEvent", LuaParameters: LuaData.Table table }
                    && table.TryGetStringValue("Message", out string? message) && message is not null)
                {
                    table.TryGetNumberValue("From", out double? fromArmy);
                    events.Add(new ReplayModeratorEvent(
                        ReplayAnalysis.GetTimestamp(replayInput),
                        replayInput.SourceId,
                        fromArmy is { } from ? (int)from : null,
                        message));
                }
            }

            return events;
        }

        /// <summary>
        /// Retrieves every order the players gave, in tick order: IssueCommand (to units) and
        /// IssueFactoryCommand (to factories), with the clicked position when the target is
        /// one. Unlike <see cref="GetMapEvents"/> this keeps orders without a position, such as
        /// factory build queues (IssueBuildFactory), which a build order needs.
        /// </summary>
        public static List<ReplayCommand> GetCommands(Replay replay) => GetCommands(replay.Body.UserInput);

        /// <inheritdoc cref="GetCommands(Replay)"/>
        public static List<ReplayCommand> GetCommands(IEnumerable<ReplayInput> inputs)
        {
            List<ReplayCommand> commands = new List<ReplayCommand>();

            foreach (ReplayInput replayInput in inputs)
            {
                (bool fromFactory, CommandData data, int unitCount) = replayInput switch
                {
                    ReplayInput.IssueCommand command => (false, command.Data, command.Units.UnitCount),
                    ReplayInput.IssueFactoryCommand factoryCommand => (true, factoryCommand.Data, factoryCommand.Factories.UnitCount),
                    _ => (false, null!, 0),
                };
                if (data is null)
                {
                    continue;
                }

                commands.Add(new ReplayCommand(
                    ReplayAnalysis.GetTimestamp(replayInput),
                    replayInput.SourceId,
                    fromFactory,
                    data.Type,
                    data.Target is CommandTarget.Position position ? new ReplayAnalysis.MapPosition(position.X, position.Z) : null,
                    string.IsNullOrEmpty(data.BlueprintId) ? null : data.BlueprintId,
                    unitCount));
            }

            return commands;
        }

        /// <summary>
        /// Retrieves every change to the count of a queued order, in tick order, resolved to
        /// the order it changes (matched on source and command identifier).
        /// </summary>
        public static List<ReplayQueueChange> GetQueueChanges(Replay replay) => GetQueueChanges(replay.Body.UserInput);

        /// <inheritdoc cref="GetQueueChanges(Replay)"/>
        public static List<ReplayQueueChange> GetQueueChanges(IEnumerable<ReplayInput> inputs)
        {
            List<ReplayQueueChange> changes = new List<ReplayQueueChange>();
            Dictionary<(int SourceId, int Identifier), CommandData> orders = new Dictionary<(int, int), CommandData>();

            foreach (ReplayInput replayInput in inputs)
            {
                switch (replayInput)
                {
                    case ReplayInput.IssueCommand command:
                        orders[(replayInput.SourceId, command.Data.Identifier)] = command.Data;
                        break;

                    case ReplayInput.IssueFactoryCommand factoryCommand:
                        orders[(replayInput.SourceId, factoryCommand.Data.Identifier)] = factoryCommand.Data;
                        break;

                    case ReplayInput.IncreaseCommandCount increase:
                        changes.Add(Resolve(replayInput, increase.CommandId, increase.Delta));
                        break;

                    case ReplayInput.DecreaseCommandCount decrease:
                        changes.Add(Resolve(replayInput, decrease.CommandId, -decrease.Delta));
                        break;
                }
            }

            return changes;

            ReplayQueueChange Resolve(ReplayInput input, int commandId, int delta)
            {
                CommandData? order = orders.GetValueOrDefault((input.SourceId, commandId));
                return new ReplayQueueChange(
                    ReplayAnalysis.GetTimestamp(input),
                    input.SourceId,
                    delta,
                    order?.Type,
                    string.IsNullOrEmpty(order?.BlueprintId) ? null : order.BlueprintId);
            }
        }

        /// <summary>
        /// Retrieves all player intents that carry a world position, in tick order, for
        /// playing a replay back on the map: commands with a clicked target position,
        /// retargeted queued commands and spawned units. Pings stay in <see cref="GetPings"/>
        /// (they carry their own type, name and colour); debug commands are excluded as
        /// camera noise.
        /// </summary>
        public static List<ReplayMapEvent> GetMapEvents(Replay replay)
        {
            List<ReplayMapEvent> events = new List<ReplayMapEvent>();

            foreach (ReplayInput replayInput in replay.Body.UserInput)
            {
                switch (replayInput)
                {
                    case ReplayInput.IssueCommand { Data: { Target: CommandTarget.Position position } data } command:
                        events.Add(new ReplayMapEvent(
                            ReplayAnalysis.GetTimestamp(replayInput),
                            replayInput.SourceId,
                            ReplayMapEventKind.Command,
                            data.Type,
                            position.X,
                            position.Z,
                            string.IsNullOrEmpty(data.BlueprintId) ? null : data.BlueprintId,
                            command.Units.UnitCount));
                        break;

                    case ReplayInput.IssueFactoryCommand { Data: { Target: CommandTarget.Position position } data } factoryCommand:
                        events.Add(new ReplayMapEvent(
                            ReplayAnalysis.GetTimestamp(replayInput),
                            replayInput.SourceId,
                            ReplayMapEventKind.FactoryCommand,
                            data.Type,
                            position.X,
                            position.Z,
                            string.IsNullOrEmpty(data.BlueprintId) ? null : data.BlueprintId,
                            factoryCommand.Factories.UnitCount));
                        break;

                    case ReplayInput.UpdateCommandTarget { Target: CommandTarget.Position position }:
                        events.Add(new ReplayMapEvent(
                            ReplayAnalysis.GetTimestamp(replayInput),
                            replayInput.SourceId,
                            ReplayMapEventKind.Retarget,
                            CommandType.None,
                            position.X,
                            position.Z,
                            null,
                            0));
                        break;

                    case ReplayInput.CreateUnit create:
                        events.Add(new ReplayMapEvent(
                            ReplayAnalysis.GetTimestamp(replayInput),
                            replayInput.SourceId,
                            ReplayMapEventKind.UnitSpawn,
                            CommandType.None,
                            create.X,
                            create.Z,
                            string.IsNullOrEmpty(create.BlueprintId) ? null : create.BlueprintId,
                            1));
                        break;
                }
            }

            return events;
        }

        /// <summary>
        /// Retrieves the notable moments in the flow of the game session, in tick order:
        /// pauses, players leaving, self-destructs and the end of the game.
        /// </summary>
        public static List<ReplaySessionEvent> GetSessionEvents(Replay replay)
        {
            List<ReplaySessionEvent> events = new List<ReplaySessionEvent>();

            foreach (ReplayInput replayInput in replay.Body.UserInput)
            {
                ReplaySessionEvent? sessionEvent = replayInput switch
                {
                    ReplayInput.RequestPause => new ReplaySessionEvent(
                        ReplayAnalysis.GetTimestamp(replayInput), replayInput.SourceId, ReplaySessionEventKind.Paused),
                    ReplayInput.RequestResume => new ReplaySessionEvent(
                        ReplayAnalysis.GetTimestamp(replayInput), replayInput.SourceId, ReplaySessionEventKind.Resumed),
                    ReplayInput.CommandSourceTerminated => new ReplaySessionEvent(
                        ReplayAnalysis.GetTimestamp(replayInput), replayInput.SourceId, ReplaySessionEventKind.PlayerLeft),
                    ReplayInput.EndGame => new ReplaySessionEvent(
                        ReplayAnalysis.GetTimestamp(replayInput), replayInput.SourceId, ReplaySessionEventKind.GameEnded),
                    ReplayInput.IssueCommand { Data.Type: CommandType.IssueKillSelf or CommandType.IssueDestroySelf } command
                        => new ReplaySessionEvent(
                            ReplayAnalysis.GetTimestamp(replayInput), replayInput.SourceId, ReplaySessionEventKind.SelfDestruct, command.Units.UnitCount),
                    _ => null,
                };

                if (sessionEvent is not null)
                {
                    events.Add(sessionEvent);
                }
            }

            return events;
        }

        /// <summary>
        /// Ping payloads carry colours as ARGB hex without a prefix (e.g. "ffe80a0a").
        /// </summary>
        private static string? ToCssColor(string? color) => color switch
        {
            { Length: 8 } => "#" + color[2..],
            { Length: 6 } => "#" + color,
            _ => null,
        };

    }
}
