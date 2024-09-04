
namespace FAForever.Replay
{
    public static class ReplaySemantics
    {

        public static List<ReplayProcessedInput> ConvertToGameEvents(List<ReplayInput> replayEvents)
        {

            List<ReplayProcessedInput> gameEvents = new List<ReplayProcessedInput>();

            // loop state
            int tick = -1;
            int source = -1;

            foreach (ReplayInput replayEvent in replayEvents)
            {
                switch (replayEvent)
                {
                    case ReplayInput.Advance e:
                        tick += e.TicksToAdvance;
                        break;
                    case ReplayInput.SetCommandSource e:
                        source = e.SourceId;
                        break;

                    default:
                        ReplayProcessedInput gameEvent = new ReplayProcessedInput(tick, source, replayEvent);
                        gameEvents.Add(gameEvent);
                        break;
                }
            }

            return gameEvents;
        }
    }
}
