
namespace FAForever.Replay
{
    public static class ReplaySemantics
    {

        public static List<GameEvent> ConvertToGameEvents(List<EventInstance> replayEvents)
        {
            
            List<GameEvent> gameEvents = new List<GameEvent>();

            // loop state
            int tick = -1;
            int source = -1;

            foreach(EventInstance replayEvent in replayEvents)
            {
                switch(replayEvent)
                {
                    case EventInstance.Advance e:
                        tick += e.TicksToAdvance;
                        break;
                    case EventInstance.SetCommandSource e:
                        source = e.SourceId;
                        break;

                    default:
                        GameEvent gameEvent = new GameEvent(tick, source, replayEvent);
                        gameEvents.Add(gameEvent);
                        break;
                }
            }

            return gameEvents;
        }
    }
}
