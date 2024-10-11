
using FAForever.Replay;

public abstract record ReplayLoadingStage
{
    public sealed record NotStarted(MemoryStream Stream) : ReplayLoadingStage;

    public sealed record WithMetadata(MemoryStream Stream, ReplayMetadata Metadata) : ReplayLoadingStage;

    public sealed record Decompressed(MemoryStream Stream, ReplayMetadata? Metadata): ReplayLoadingStage;

    public sealed record WithScenario(ReplayBinaryReader Stream, ReplayMetadata? Metadata, ReplayHeader Header) : ReplayLoadingStage;

    public sealed record AtInput(ReplayBinaryReader Stream, ReplayMetadata? Metadata, ReplayHeader Header, ReplayBodyInvariant BodyInvariant) : ReplayLoadingStage;

    public sealed record Complete(ReplayBinaryReader Stream, ReplayMetadata? Metadata, ReplayHeader Header, ReplayBody Body) : ReplayLoadingStage;

    public sealed record Failed(string Message) : ReplayLoadingStage;
}