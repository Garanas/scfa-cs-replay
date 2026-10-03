using System.Reflection;

namespace FAForever.Replay.Viewer.Services;

/// <summary>
/// The commit this build was made from. The SDK appends <c>SourceRevisionId</c> to the
/// informational version (<c>1.0.0+&lt;sha&gt;</c>): from git in a local build, from the
/// <c>SOURCE_REVISION</c> build argument in the container image (which has no .git).
/// </summary>
public static class BuildInfo
{
    public const string Repository = "https://github.com/Garanas/scfa-cs-replay";

    /// <summary>The full commit hash, or null when the build did not know it.</summary>
    public static string? Commit { get; } = ReadCommit();

    public static string? ShortCommit => Commit?[..Math.Min(7, Commit.Length)];

    public static string? CommitUrl => Commit is null ? null : $"{Repository}/commit/{Commit}";

    private static string? ReadCommit()
    {
        string? version = typeof(BuildInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        int plus = version?.IndexOf('+') ?? -1;
        return plus < 0 || plus == version!.Length - 1 ? null : version[(plus + 1)..];
    }
}
