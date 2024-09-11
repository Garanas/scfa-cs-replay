
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

            foreach(ReplayProcessedInput replayInput in replay.Events)
            {
                switch (replayInput.instance)
                {
                    case ReplayInput.SimCallback callback when callback.Endpoint == "GiveResourcesToPlayer" && callback.LuaParameters is LuaData.Table:

                        // Don't ask - this is how it works.
                        if (!(callback.LuaParameters is LuaData.Table luaTable) ||
                            !(luaTable.Value.TryGetValue("From", out LuaData? luaFrom) && luaFrom is LuaData.Number from)  ||
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

    }
}
