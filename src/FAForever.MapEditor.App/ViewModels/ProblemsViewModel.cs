using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.Project.Checks;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// A line of the Problems tab.
/// </summary>
/// <param name="Index">Its place in the list, for <c>problems.show</c>.</param>
public sealed record ProblemItem(int Index, Problem Problem)
{
    public string Message => Problem.Message;

    public string SeverityText => Problem.Severity.ToString();

    public bool IsError => Problem.Severity == ProblemSeverity.Error;

    public bool IsWarning => Problem.Severity == ProblemSeverity.Warning;

    /// <summary>
    /// Whether going to it leads anywhere: a thing or a place.
    /// </summary>
    public bool HasPlace => Problem.Thing is not null || Problem.Place is not null;
}

/// <summary>
/// The Problems tab (user story F-14): what the checks (<see cref="ProblemChecks"/>) find wrong or
/// doubtful about the open map, worst first. The checks run a moment after each step of undo (not
/// each dab), before a save or export, and when asked (<c>plugins.check</c>); they are cheap and run
/// on the window's thread.
/// </summary>
public sealed partial class ProblemsViewModel : ObservableObject
{
    /// <summary>
    /// How long the map must stay unchanged after a step before the checks run.
    /// </summary>
    public static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(300);

    private readonly Func<ProblemContext?> _context;
    private readonly SynchronizationContext? _ui;
    private readonly ILogger _log;
    private int _scheduled;

    /// <param name="context">What the checks read now; null without a map.</param>
    /// <param name="ui">The window's thread; null in tests, where <see cref="Schedule"/> runs at once.</param>
    public ProblemsViewModel(Func<ProblemContext?> context, SynchronizationContext? ui, ILogger log)
    {
        _context = context;
        _ui = ui;
        _log = log;
    }

    /// <summary>
    /// The checks; a feature or plugin registers its own.
    /// </summary>
    public ProblemChecks Checks { get; } = new ProblemChecks();

    /// <summary>
    /// What the checks found last, worst first.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Errors), nameof(Warnings), nameof(Infos), nameof(Header), nameof(Summary), nameof(IsEmpty))]
    public partial IReadOnlyList<ProblemItem> Items { get; private set; } = [];

    public int Errors => Items.Count(item => item.Problem.Severity == ProblemSeverity.Error);

    public int Warnings => Items.Count(item => item.Problem.Severity == ProblemSeverity.Warning);

    public int Infos => Items.Count(item => item.Problem.Severity == ProblemSeverity.Info);

    public bool IsEmpty => Items.Count == 0;

    /// <summary>
    /// The tab's header, with the errors and warnings counted.
    /// </summary>
    public string Header => Errors + Warnings > 0 ? $"Problems ({Errors + Warnings})" : "Problems";

    /// <summary>
    /// E.g. <c>1 error, 2 warnings, 1 note</c>, or <c>No problems found</c>.
    /// </summary>
    public string Summary => IsEmpty
        ? "No problems found"
        : string.Join(", ", new[] { (Errors, "error"), (Warnings, "warning"), (Infos, "note") }
            .Where(count => count.Item1 > 0)
            .Select(count => $"{count.Item1} {count.Item2}{(count.Item1 == 1 ? "" : "s")}"));

    /// <summary>
    /// Runs every check now.
    /// </summary>
    /// <returns>What they found.</returns>
    public IReadOnlyList<Problem> Run()
    {
        using Performance.Span measure = Performance.Measure("model.problems");
        Interlocked.Increment(ref _scheduled);
        if (_context() is not ProblemContext context)
        {
            Items = [];
            return [];
        }
        List<FailedCheck> failed = [];
        IReadOnlyList<Problem> problems = Checks.Run(context, failed);
        foreach (FailedCheck check in failed)
        {
            _log.LogError(check.Exception, "The check {Check} failed: {Reason}", check.Check.Id, check.Exception.Message);
        }
        Items = [.. problems.Select((problem, index) => new ProblemItem(index, problem))];
        return problems;
    }

    /// <summary>
    /// Runs the checks once the map has stayed unchanged for <see cref="Quiet"/>: after a step of
    /// undo. Without a window (tests) they run at once.
    /// </summary>
    public void Schedule()
    {
        if (_ui is null)
        {
            Run();
            return;
        }
        int ticket = Interlocked.Increment(ref _scheduled);
        _ = Task.Delay(Quiet).ContinueWith(_ => UiThread.Run(_ui, () =>
        {
            if (ticket == Volatile.Read(ref _scheduled))
            {
                Run();
            }
        }), TaskScheduler.Default);
    }

    /// <summary>
    /// Forgets the problems: no map is open.
    /// </summary>
    public void Clear()
    {
        Interlocked.Increment(ref _scheduled);
        Items = [];
    }
}
