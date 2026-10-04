namespace FAForever.Vault.Viewer.Services.Replays;

/// <summary>
/// Where a loaded replay came from: the FAForever vault or a local file.
/// </summary>
public abstract record ReplayOrigin
{
    public sealed record Vault(int ReplayId) : ReplayOrigin;

    public sealed record LocalFile(string FileName) : ReplayOrigin;
}
