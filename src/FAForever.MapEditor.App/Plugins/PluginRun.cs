using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Plugins;

/// <summary>
/// What a plugin's Lua may do while it runs: read a document, and when it runs a command, edit the
/// map and run the editor's commands. Checks and events only read.
/// </summary>
/// <param name="What">What runs, for errors: <c>the command Scatter</c>, <c>a check</c>.</param>
/// <param name="Document">What reads see: the run's document, the checked one; null without a map.</param>
/// <param name="Run">The run's edits; null where the plugin may only read.</param>
/// <param name="Clock">The time limit of the call, paused while a command the plugin runs works.</param>
internal sealed record PluginContext(string What, Func<MapDocument?> Document, PluginClock Clock, PluginRun? Run = null)
{
    /// <summary>
    /// Whether the plugin's script is being loaded: the only time it may register things.
    /// </summary>
    public bool IsLoading { get; init; }

    public MapDocument RequireDocument() => Document() ?? throw new InvalidOperationException("No map is open");

    public PluginRun RequireRun() => Run ?? throw new InvalidOperationException($"{What} may only read the map; edits and commands are for a plugin's commands");
}

/// <summary>
/// The time limit of one call into a plugin's Lua: a runaway script is cancelled when its time is
/// up. The time a command it runs takes (a save, an export) does not count.
/// </summary>
internal sealed class PluginClock : IDisposable
{
    private readonly CancellationTokenSource _cancel = new CancellationTokenSource();
    private readonly System.Diagnostics.Stopwatch _watch = System.Diagnostics.Stopwatch.StartNew();
    private readonly TimeSpan _limit;

    public PluginClock(TimeSpan limit)
    {
        _limit = limit;
        _cancel.CancelAfter(limit);
    }

    public CancellationToken Token => _cancel.Token;

    public TimeSpan Limit => _limit;

    public bool IsUp => _cancel.IsCancellationRequested;

    /// <summary>
    /// Stops the time while the editor works for the plugin.
    /// </summary>
    public void Pause()
    {
        _watch.Stop();
        if (!_cancel.IsCancellationRequested)
        {
            _cancel.CancelAfter(Timeout.InfiniteTimeSpan);
        }
    }

    public void Resume()
    {
        _watch.Start();
        if (!_cancel.IsCancellationRequested)
        {
            TimeSpan left = _limit - _watch.Elapsed;
            _cancel.CancelAfter(left > TimeSpan.Zero ? left : TimeSpan.FromMilliseconds(1));
        }
    }

    public void Dispose() => _cancel.Dispose();
}

/// <summary>
/// A run of a plugin's command as one step of undo (user story F-15): a group of the session
/// (<see cref="EditGroup"/>) holding its own edits and the steps of the commands it runs. The edits
/// collect in a transaction, which ends before each command the plugin runs (that command then
/// reads them), so a run of a thousand edits is one transaction, not a thousand steps. A run that
/// fails is thrown away whole.
/// </summary>
internal sealed class PluginRun : IDisposable
{
    private readonly EditSession _session;
    private readonly EditGroup _group;
    private readonly string _description;
    private EditTransaction? _pending;

    /// <exception cref="InvalidOperationException">A transaction or group is open (a stroke, another run).</exception>
    public PluginRun(EditSession session, string description)
    {
        _session = session;
        _description = description;
        _group = session.BeginGroup(description);
    }

    /// <summary>
    /// The map with the run's edits so far.
    /// </summary>
    public MapDocument Document => _pending?.Document ?? _session.Document;

    /// <summary>
    /// The transaction the run's own edits go into, opened at the first.
    /// </summary>
    public EditTransaction Transaction => _pending ??= _session.Begin(_description);

    public void Edit(Func<MapDocument, MapDocument> edit) => Transaction.Apply(edit);

    /// <summary>
    /// Ends the transaction of the run's own edits, as a step of the group: before a command runs.
    /// </summary>
    public void Flush()
    {
        _pending?.Commit();
        _pending = null;
    }

    /// <summary>
    /// Keeps the run as one step.
    /// </summary>
    /// <returns>Whether the map changed.</returns>
    public bool Commit()
    {
        Flush();
        return _group.Commit();
    }

    /// <summary>
    /// Throws the run away unless it was committed.
    /// </summary>
    public void Dispose()
    {
        _pending?.Dispose();
        _pending = null;
        _group.Dispose();
    }
}
