
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
