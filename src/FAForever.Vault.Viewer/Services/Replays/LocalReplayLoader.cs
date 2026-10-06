using Microsoft.AspNetCore.Components;

namespace FAForever.Vault.Viewer.Services.Replays;

/// <summary>
/// Opens a replay file from this computer: reads it into memory, parses it in the browser, keeps it
/// in <see cref="ReplaySessionState"/> and goes to <c>replays/local</c>. Shared by every way in: a file
/// picked or dropped, a file opened with the installed app, and a file from the replay folder.
/// </summary>
public sealed class LocalReplayLoader(ReplayLoadingService loader, ReplaySessionState session, NavigationManager navigation)
{
    /// <summary>The local replay page.</summary>
    public const string LocalPage = "replays/local";

    /// <summary>Generous cap: the largest known .scfareplay files are tens of MB.</summary>
    public const long MaxReplayFileBytes = 256L * 1024 * 1024;

    /// <summary>
    /// Loads the file and opens it on the local replay page. Returns why it could not be opened, or
    /// null when it was (or when the load was cancelled).
    /// </summary>
    public async Task<string?> OpenAsync(string fileName, Func<CancellationToken, Task<Stream>> openRead, IProgress<ReplayLoadProgress>? progress, CancellationToken cancellationToken)
    {
        if (ReplayFormats.FromFileName(fileName) is not ReplayFormat format)
        {
            return $"'{fileName}' is not a .fafreplay or .scfareplay file.";
        }

        try
        {
            progress?.Report(new ReplayLoadProgress("Reading file", null));

            using MemoryStream buffer = new();
            await using (Stream source = await openRead(cancellationToken))
            {
                await source.CopyToAsync(buffer, cancellationToken);
            }
            buffer.Position = 0;

            switch (await loader.LoadAsync(buffer, format, progress, cancellationToken))
            {
                case ReplayLoadResult.Success success:
                    session.Set(new LoadedReplay(success.Replay, success.Metadata, new ReplayOrigin.LocalFile(fileName)));
                    navigation.NavigateTo(LocalPage);
                    return null;
                case ReplayLoadResult.Failure failure:
                    return failure.Message;
                default:
                    return "The replay could not be read.";
            }
        }
        catch (OperationCanceledException)
        {
            // The user cancelled; nothing to report.
            return null;
        }
        catch (Exception exception)
        {
            return $"Could not read the file: {exception.Message}";
        }
    }
}
