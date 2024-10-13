
using System.Collections.ObjectModel;

namespace FAForever.Replay.Viewer
{
    public record ReplayState(ReplayType replayType, ReadOnlyCollection<ReplayChatMessage> chatMessages)
    {

        public ReplayType ReplayType { get; init; } = replayType;

        public ReadOnlyCollection<ReplayChatMessage> ChatMessages { get; } = chatMessages;

    }
}