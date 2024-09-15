
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

            foreach (ReplayProcessedInput replayInput in replay.Body.UserInput)
            {
                switch (replayInput.instance)
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

            foreach (ReplayProcessedInput replayInput in replay.Body.UserInput)
            {
                string key = "Unknown";
                switch (replayInput.instance)
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

                    case ReplayInput.VerifyChecksum:
                        key = "VerifyChecksum";
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

    }
}
