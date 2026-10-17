namespace FAForever.Vault.Viewer.Tests.Players;

/// <summary>A clock that stands still, so the sample API's shifted dates are the same on every run.</summary>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
