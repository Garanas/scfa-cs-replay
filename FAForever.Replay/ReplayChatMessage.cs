
namespace FAForever.Replay
{
    public record ReplayChatMessage(TimeSpan Timestamp, string Sender, string Receiver, string Message);
}
