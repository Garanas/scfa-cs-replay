
namespace FAForever.Replay
{
    public record Replay(ReplayHeader Header, List<ReplayProcessedInput> Events);
}
