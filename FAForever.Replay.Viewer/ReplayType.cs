
namespace FAForever.Replay.Viewer
{
    public interface ReplayType
    {
        public record FAForever(string replayId) : ReplayType {
            public string ReplayId {get;init;} = replayId;
        }

        public record SCFA() : ReplayType;
    }
}