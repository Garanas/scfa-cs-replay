namespace FAForever.Replay.Viewer.Services.Replays;

/// <summary>
/// Holds the replay the user is currently looking at. Pages subscribe to <see cref="Changed"/>.
/// </summary>
public sealed class ReplaySessionState
{
    public LoadedReplay? Current { get; private set; }

    public event Action? Changed;

    public void Set(LoadedReplay replay)
    {
        Current = replay;
        Changed?.Invoke();
    }

    public void Clear()
    {
        Current = null;
        Changed?.Invoke();
    }
}
