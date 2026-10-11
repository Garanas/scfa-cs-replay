using System.Collections.Immutable;
using System.Diagnostics;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// Results that regenerate on their own (the Procedural mode): for a generator the map turns this
/// on for (<see cref="ProceduralSettings.AutoGenerate"/>), while the views show its result
/// (<see cref="ShowsWhen"/>) and the editor lets results regenerate on their own
/// (<see cref="IsOn"/>), a change to one of its inputs (<see cref="IGenerator.Inputs"/>) starts a
/// timer of <see cref="Delay"/> again; when it runs out the generator runs on a thread of the pool
/// from the map as it is then, and a newer change to its inputs stops that run
/// (<see cref="IGenerator.Generate(MapDocument, CancellationToken)"/>) and starts the timer again.
/// The result is kept as any other (<see cref="GeneratedResults.Keep"/>); one that is part of the
/// map (<see cref="IDocumentGenerator"/>) goes into it through <see cref="Apply"/>, without a step
/// of undo. Every registered generator may regenerate on its own.
/// </summary>
public sealed class AutoGeneration
{
    private readonly GenerationViewModel _generation;
    private readonly Func<MapDocument?> _document;
    private readonly SynchronizationContext? _ui;
    private readonly ILogger _log;
    private readonly Dictionary<string, Pending> _pending = new Dictionary<string, Pending>(StringComparer.Ordinal);
    private readonly Dictionary<string, (Func<MapDocument, bool> Shown, string When)> _shown = new Dictionary<string, (Func<MapDocument, bool>, string)>(StringComparer.Ordinal);
    private bool _isOn = true;

    // what one generator waits for or runs: the inputs it was scheduled for, and how to stop it
    private sealed class Pending(ImmutableArray<object?> inputs, CancellationTokenSource cancel)
    {
        public ImmutableArray<object?> Inputs { get; set; } = inputs;

        public CancellationTokenSource Cancel { get; set; } = cancel;

        public bool Running { get; set; }

        public Task Task { get; set; } = Task.CompletedTask;
    }

    /// <param name="document">The open map's document, or null.</param>
    /// <param name="ui">The window's thread, where results are kept; null in tests.</param>
    public AutoGeneration(GenerationViewModel generation, Func<MapDocument?> document, SynchronizationContext? ui, ILogger log)
    {
        _generation = generation;
        _document = document;
        _ui = ui;
        _log = log;
    }

    /// <summary>
    /// How long after the last change to its inputs a result regenerates: a second.
    /// </summary>
    public TimeSpan Delay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The generators that may regenerate on their own: every one registered.
    /// </summary>
    public IReadOnlyList<string> Allowed => [.. _generation.Results.Generators.Select(generator => generator.Id)];

    /// <summary>
    /// Whether the editor lets results regenerate on their own (the switch in the views' bar, the
    /// user's setting); off, nothing waits or runs, and turning it on again looks at the map.
    /// </summary>
    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (_isOn == value)
            {
                return;
            }
            _isOn = value;
            if (value)
            {
                Follow();
            }
            else
            {
                StopAll();
            }
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Says when the views show a generator's result: only then does it regenerate on its own.
    /// Without one it always does.
    /// </summary>
    /// <param name="when">The condition in words, for the Procedural inspector: <c>while the Walkability chip is on</c>.</param>
    public void ShowsWhen(string id, Func<MapDocument, bool> shown, string when) => _shown[id] = (shown, when);

    /// <summary>
    /// Whether the views show a generator's result in a version of the map.
    /// </summary>
    public bool IsShown(string id, MapDocument document) => !_shown.TryGetValue(id, out var shown) || shown.Shown(document);

    /// <summary>
    /// When a generator's result shows (<see cref="ShowsWhen"/>), in words; null when always.
    /// </summary>
    public string? ShownWhen(string id) => _shown.TryGetValue(id, out var shown) ? shown.When : null;

    /// <summary>
    /// Puts a result that is part of the map (<see cref="IDocumentGenerator"/>) into it, on the
    /// window's thread, after it was kept; without it such a result is only kept.
    /// </summary>
    public Action<IDocumentGenerator, object>? Apply { get; set; }

    /// <summary>
    /// Whether a generator waits for its timer or runs.
    /// </summary>
    public bool IsPending(string id) => _pending.ContainsKey(id);

    /// <summary>
    /// The generators whose timer runs, in the order they were scheduled.
    /// </summary>
    public IReadOnlyList<string> Waiting => [.. _pending.Where(pair => !pair.Value.Running).Select(pair => pair.Key)];

    /// <summary>
    /// The generators running on their own now.
    /// </summary>
    public IReadOnlyList<string> Running => [.. _pending.Where(pair => pair.Value.Running).Select(pair => pair.Key)];

    /// <summary>
    /// The generators the map lets regenerate on their own whose result is out of date or missing
    /// while the views do not show it: they wait until it shows.
    /// </summary>
    public IReadOnlyList<string> Hidden
    {
        get
        {
            if (_document() is not MapDocument document)
            {
                return [];
            }
            return [.. document.Procedural.AutoGenerate.Where(id => _generation.Results.Find(id) is not null && !IsShown(id, document)
                && (_generation.Results.ResultOf(id) is not GeneratedResult result || result.IsStale(document)))];
        }
    }

    /// <summary>
    /// How many runs finished and were kept, and how many a newer change stopped (for automation and tests).
    /// </summary>
    public int Kept { get; private set; }

    public int Cancelled { get; private set; }

    /// <summary>
    /// Raised on the window's thread when a result regenerated on its own was kept.
    /// </summary>
    public event Action<string>? Generated;

    /// <summary>
    /// Raised when what waits or runs changed, or the switch (<see cref="IsOn"/>).
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Finishes when nothing waits or runs any more (tests).
    /// </summary>
    public async Task WhenIdleAsync()
    {
        while (_pending.Values.Select(pending => pending.Task).ToArray() is { Length: > 0 } tasks)
        {
            await Task.WhenAll(tasks);
        }
    }

    /// <summary>
    /// Looks at the map after a change (every step, not every dab) or a change of what the views
    /// show: starts or restarts the timer of each generator that regenerates on its own, whose
    /// result shows and whose inputs changed, and stops what no longer regenerates on its own (a
    /// timer of a result no longer shown stops too; a run goes on to its end).
    /// </summary>
    public void Follow()
    {
        using Performance.Span measure = Performance.Measure("model.generate-auto-follow");
        int before = _pending.Count;
        MapDocument? document = _document();
        foreach ((string id, Pending pending) in _pending.ToArray())
        {
            if (document is null || !_isOn || !document.Procedural.RegeneratesOnItsOwn(id) || (!pending.Running && !IsShown(id, document)))
            {
                Stop(id);
            }
        }
        if (document is not null && _isOn)
        {
            foreach (string id in document.Procedural.AutoGenerate)
            {
                if (_generation.Results.Find(id) is not IGenerator generator || !IsShown(id, document))
                {
                    continue;
                }
                ImmutableArray<object?> inputs = [.. generator.Inputs.Select(input => input.Read(document))];
                if (_pending.TryGetValue(id, out Pending? pending))
                {
                    if (SameInputs(pending.Inputs, inputs))
                    {
                        continue;
                    }
                    // a newer change: the run stops, the timer starts again
                    Stop(id);
                }
                else if (_generation.Results.ResultOf(id) is GeneratedResult result && !result.IsStale(document))
                {
                    continue;
                }
                Schedule(generator, inputs);
            }
        }
        if (before != 0 || _pending.Count != 0)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Stops a generator's timer or run (another map, the setting turned off, a person's Generate).
    /// </summary>
    public void Stop(string id)
    {
        if (_pending.Remove(id, out Pending? pending))
        {
            if (pending.Running)
            {
                Cancelled++;
            }
            pending.Cancel.Cancel();
            if (pending.Running)
            {
                _generation.SetRunning(id, false);
            }
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Stops everything: another map is open.
    /// </summary>
    public void StopAll()
    {
        foreach (string id in _pending.Keys.ToArray())
        {
            Stop(id);
        }
    }

    private static bool SameInputs(ImmutableArray<object?> a, ImmutableArray<object?> b) =>
        a.Length == b.Length && a.Zip(b).All(pair => GeneratedInput.Same(pair.First, pair.Second));

    private void Schedule(IGenerator generator, ImmutableArray<object?> inputs)
    {
        CancellationTokenSource cancel = new CancellationTokenSource();
        Pending pending = new Pending(inputs, cancel);
        _pending[generator.Id] = pending;
        pending.Task = RunAfterDelayAsync(generator, pending);
    }

    private async Task RunAfterDelayAsync(IGenerator generator, Pending pending)
    {
        CancellationToken token = pending.Cancel.Token;
        try
        {
            await Task.Delay(Delay, token).ConfigureAwait(false);
            TaskCompletionSource<MapDocument?> start = new TaskCompletionSource<MapDocument?>();
            // the run starts from the map as it is on the window's thread
            UiThread.Run(_ui, () =>
            {
                if (token.IsCancellationRequested || !_pending.TryGetValue(generator.Id, out Pending? now) || now != pending || _generation.IsRunning(generator.Id))
                {
                    start.SetResult(null);
                    return;
                }
                pending.Running = true;
                _generation.SetRunning(generator.Id, true);
                _generation.Results.HandResults(generator);
                Changed?.Invoke();
                start.SetResult(_document());
            });
            if (await start.Task.ConfigureAwait(false) is not MapDocument document)
            {
                Finish(generator.Id, pending, null, null);
                return;
            }
            long begin = Stopwatch.GetTimestamp();
            object value = await Task.Run(() =>
            {
                using Performance.Span measure = Performance.Measure("model.generate-auto");
                return generator.Generate(document, token);
            }, token).ConfigureAwait(false);
            _log.LogInformation("Regenerated the {Generator} on its own ({Milliseconds} ms)", generator.Id, Math.Round(Stopwatch.GetElapsedTime(begin).TotalMilliseconds));
            Finish(generator.Id, pending, document, value, Stopwatch.GetElapsedTime(begin));
        }
        catch (OperationCanceledException)
        {
            // a newer change or another map: the run that follows makes the result
        }
        catch (Exception exception)
        {
            _log.LogError(exception, "Regenerating the {Generator} on its own failed: {Reason}", generator.Id, exception.Message);
            Finish(generator.Id, pending, null, null);
        }
    }

    private void Finish(string id, Pending pending, MapDocument? document, object? value, TimeSpan took = default) => UiThread.Run(_ui, () =>
    {
        if (!_pending.TryGetValue(id, out Pending? now) || now != pending)
        {
            // stopped meanwhile
            return;
        }
        _pending.Remove(id);
        if (pending.Running)
        {
            _generation.SetRunning(id, false);
        }
        if (document is not null && value is not null && !pending.Cancel.IsCancellationRequested && _generation.Results.Find(id) is IGenerator generator)
        {
            _generation.Results.Keep(GeneratedResult.Of(generator, document, value));
            _generation.NoteMade(id, took, GenerationWay.OnItsOwn);
            Kept++;
            if (generator is IDocumentGenerator part && Apply is not null)
            {
                // into the map as it is now, without a step of undo
                Apply(part, value);
            }
            _generation.Refresh(_document());
            Generated?.Invoke(id);
        }
        else
        {
            _generation.Refresh(_document());
        }
        Changed?.Invoke();
        // a change while it ran that left the inputs as they were is current; one that did not starts again
        Follow();
    });
}
