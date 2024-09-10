
namespace FAForever.Replay
{
    public static class ReplaySemantics
    {

        /// <summary>
        /// Retrieves all the chat messages from the replay input.
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public static List<ReplayChatMessage> GetChatMessages(ReadOnlySpan<ReplayProcessedInput> input)
        {
            List<ReplayChatMessage> chatMessages = new List<ReplayChatMessage>();

            foreach(ReplayProcessedInput replayInput in input)
            {
                switch (replayInput.instance)
                {
                    case ReplayInput.SimCallback callback when callback.Endpoint == "GiveResourcesToPlayer" && callback.LuaParameters is LuaData.Table:
                         
                        if (callback.Endpoint != "GiveResourcesToPlayer")
                        {
                            break;
                        }

                        // Don't ask - this is how it works.
                        if (callback.LuaParameters is not LuaData.Table luaTable ||
                            luaTable.Value["From"] is not LuaData.Number from ||
                            luaTable.Value["Sender"] is not LuaData.String sender ||
                            luaTable.Value["Msg"] is not LuaData.Table luaMsgTable ||
                            luaMsgTable.Value["to"] is not LuaData.String to ||
                            luaMsgTable.Value["msg"] is not LuaData.String msg)
                        {
                            break;
                        }

                        chatMessages.Add(new ReplayChatMessage(TimeSpan.FromSeconds(replayInput.Tick), sender.Value, to.Value, msg.Value));

                        break;

                    default:
                        break;
                }
            }

            return chatMessages;
        }

    }
}
