namespace FAForever.Replay.Viewer.Services.Replays;

/// <summary>
/// A fully parsed replay together with the FAForever metadata (absent for .scfareplay files)
/// and its origin.
/// </summary>
public sealed record LoadedReplay(Replay Replay, ReplayMetadata? Metadata, ReplayOrigin Origin);
