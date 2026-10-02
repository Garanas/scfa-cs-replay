namespace FAForever.Replay.Viewer.Services.Replays;

public sealed record ReplayLoadProgress(string Phase, int? Percent);

public abstract record ReplayLoadResult
{
    public sealed record Success(Replay Replay, ReplayMetadata? Metadata) : ReplayLoadResult;

    public sealed record Failure(string Message) : ReplayLoadResult;
}

/// <summary>
/// Drives the incremental <see cref="ReplayLoader.ProcessReplayStage(ReplayLoadingStage.NotStarted)"/>
/// state machine. WebAssembly is single threaded, so between stages (and between input batches)
/// we yield to the browser to keep the UI responsive; <see cref="ReplayLoadProgress"/> reports
/// the phase and, while parsing inputs, the percentage processed.
/// </summary>
public sealed class ReplayLoadingService
{
    public async Task<ReplayLoadResult> LoadAsync(MemoryStream stream, ReplayFormat format, IProgress<ReplayLoadProgress>? progress, CancellationToken cancellationToken)
    {
        try
        {
            ReplayLoadingStage stage = format == ReplayFormat.FAForever
                ? new ReplayLoadingStage.NotStarted(stream)
                : new ReplayLoadingStage.Decompressed(stream, null);

            ReplayMetadata? metadata = null;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                switch (stage)
                {
                    case ReplayLoadingStage.NotStarted notStarted:
                        progress?.Report(new ReplayLoadProgress("Reading metadata", null));
                        stage = ReplayLoader.ProcessReplayStage(notStarted);
                        break;

                    case ReplayLoadingStage.WithMetadata withMetadata:
                        metadata = withMetadata.Metadata;
                        progress?.Report(new ReplayLoadProgress("Decompressing", null));
                        stage = ReplayLoader.ProcessReplayStage(withMetadata);
                        break;

                    case ReplayLoadingStage.Decompressed decompressed:
                        metadata ??= decompressed.Metadata;
                        progress?.Report(new ReplayLoadProgress("Reading scenario", null));
                        stage = ReplayLoader.ProcessReplayStage(decompressed);
                        break;

                    case ReplayLoadingStage.WithScenario withScenario:
                        progress?.Report(new ReplayLoadProgress("Parsing commands", 0));
                        stage = ReplayLoader.ProcessReplayStage(withScenario);
                        break;

                    case ReplayLoadingStage.AtInput atInput:
                        progress?.Report(new ReplayLoadProgress("Parsing commands", atInput.BodyInvariant.PercentageProcessed));
                        stage = ReplayLoader.ProcessReplayStage(atInput);
                        break;

                    case ReplayLoadingStage.Complete complete:
                        progress?.Report(new ReplayLoadProgress("Complete", 100));
                        return new ReplayLoadResult.Success(new Replay(complete.Header, complete.Body), metadata);

                    case ReplayLoadingStage.Failed failed:
                        return new ReplayLoadResult.Failure(failed.Message);

                    default:
                        return new ReplayLoadResult.Failure("The replay loader entered an unknown stage.");
                }

                // Yield to the browser so the progress UI can repaint.
                await Task.Delay(1, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // The loader throws on unknown input types and malformed Lua data.
            return new ReplayLoadResult.Failure($"The replay could not be parsed: {exception.Message}");
        }
    }
}
