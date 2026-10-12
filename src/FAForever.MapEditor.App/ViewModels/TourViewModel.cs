using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Windows.Input;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.Tours;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Workspaces;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// A tour in the list of the Tours page.
/// </summary>
public sealed class TourListItem(TourEntry entry, DateTimeOffset? finished, ICommand start)
{
    public TourEntry Entry => entry;

    public string Id => entry.Tour?.Id ?? System.IO.Path.GetFileNameWithoutExtension(entry.File);

    public string Title => entry.Title;

    public string Summary => entry.Tour?.Summary ?? "";

    /// <summary>
    /// E.g. <c>12 steps</c>.
    /// </summary>
    public string StepsText => entry.Tour is { } tour ? $"{tour.Steps.Count} step{(tour.Steps.Count == 1 ? "" : "s")}" : "";

    public bool IsFinished => finished is not null;

    public string FinishedText => finished is DateTimeOffset when ? $"Finished {when.LocalDateTime:d MMMM yyyy}" : "";

    public string? Error => entry.Error;

    public bool HasError => entry.Error is not null;

    public bool CanStart => entry.IsOk;

    public ICommand StartCommand => start;

    /// <summary>
    /// What a screen reader says of the line.
    /// </summary>
    public string Label => entry.IsOk ? $"{Title}: {Summary} ({StepsText}{(IsFinished ? ", finished" : "")})" : $"{Title}: cannot be used: {Error}";
}

/// <summary>
/// How the person answers the offer of the window's tour.
/// </summary>
public enum TourOffer
{
    /// <summary>Start the tour; it is not offered again.</summary>
    Start,

    /// <summary>Not now: offered again at the next start.</summary>
    Later,

    /// <summary>Don't ask again.</summary>
    Never,
}

/// <summary>
/// The guided tours (user story H-1): the catalogue read from the workspace's <c>tours</c> folder, the tour and
/// step that run, what each step waits for, what to put back when it ends, and the offer made once to a person
/// new to the editor. The overlay (<c>Views/TourOverlay</c>) draws the step; this holds what it shows.
/// </summary>
/// <remarks>
/// A tour's actions (<c>start</c>, <c>before</c>, <c>doItForMe</c>) run through the registry without a person, so
/// they ask nothing; <see cref="TourRefusals"/> lists what they never run. A step that asks the person to act
/// (<see cref="TourStep.Until"/>) is checked when a command completes, the document changes or the view, mode or
/// tool changes, at most every 250 ms (<c>tour.condition</c>), reading only <see cref="TourState"/>.
/// </remarks>
public sealed partial class TourViewModel : ObservableObject
{
    /// <summary>
    /// The id of the tour that is offered on the first start.
    /// </summary>
    public const string OfferedTour = "window";

    private static readonly TimeSpan ConditionInterval = TimeSpan.FromMilliseconds(250);

    private readonly MainViewModel _model;
    private readonly SynchronizationContext? _ui;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private ProjectWatcher? _watcher;
    private TimeSpan _lastCheck = TimeSpan.FromSeconds(-1);
    private bool _checkPending;
    private bool _offerAnswered;
    private bool _offerDismissed;
    private TourRestore? _restore;

    public TourViewModel(MainViewModel model)
    {
        _model = model;
        _ui = SynchronizationContext.Current;
        Log = model.Logger("Tours");
        Ledger = new TourLayoutLedger(model.Layout);
        model.Events.WorkspaceOpened += workspace => Open(workspace.ToursFolder);
        model.Events.MapOpened += _ => ConsiderOffer();
        model.Events.DocumentChanged += _ => Poke();
        model.Commands.Completed += _ => Poke();
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.View) or nameof(MainViewModel.SelectedMode) or nameof(MainViewModel.ActiveTool) or nameof(MainViewModel.Dock))
            {
                Poke();
            }
        };
        model.Layout.Changed += (_, _) => Poke();
        model.Layout.PlacesChanged += (_, _) => Poke();
        model.Palette.PropertyChanged += (_, _) => Poke();
        // the stratum or mask chosen in a panel's list is no command
        model.StrataTool.PropertyChanged += (_, _) => Poke();
        model.Masks.PropertyChanged += (_, _) => Poke();
        // another map: a tour about the old one ends
        model.MapChanged += (_, _) =>
        {
            if (Running is not null && Running.Needs.Contains("Map", StringComparer.OrdinalIgnoreCase))
            {
                _ = EndAsync();
            }
            ConsiderOffer();
        };
    }

    public ILogger Log { get; }

    /// <summary>
    /// What the tour changed in the layout, put back when it ends.
    /// </summary>
    public TourLayoutLedger Ledger { get; }

    /// <summary>
    /// Draws the step that runs: finds its target, ringing it and placing the card. The window's overlay
    /// sets it; commands wait for it, so the state after one holds the place.
    /// </summary>
    public Func<Task>? Stage { get; set; }

    /// <summary>
    /// The workspace's <c>tours</c> folder; null before the workspace is ready.
    /// </summary>
    [ObservableProperty]
    public partial string? Folder { get; private set; }

    /// <summary>
    /// The tour files of the folder: the tours that can be used, then those that cannot, with why.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<TourEntry> Entries { get; private set; } = [];

    /// <summary>
    /// The tours for the Tours page.
    /// </summary>
    public ObservableCollection<TourListItem> Items { get; } = [];

    /// <summary>
    /// What the workspace remembers: whether the offer was answered, the tours finished.
    /// </summary>
    public TourProgress Progress { get; private set; } = new TourProgress();

    /// <summary>
    /// Whether the Tours page shows over the editor.
    /// </summary>
    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    public bool HasErrors => Entries.Any(entry => !entry.IsOk);

    public bool IsEmpty => Entries.Count == 0;

    #region The tour that runs

    /// <summary>
    /// The tour that runs, or null.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning), nameof(StepCountText), nameof(StepTitle), nameof(StepText), nameof(NextText), nameof(CanGoBack))]
    public partial Tour? Running { get; private set; }

    /// <summary>
    /// The step that shows, or null.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StepCountText), nameof(StepTitle), nameof(StepText), nameof(HasDoItForMe), nameof(IsWaiting), nameof(ShowsDone), nameof(NextText), nameof(CanGoBack))]
    public partial TourStep? Step { get; private set; }

    /// <summary>
    /// The step's number from 0.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StepCountText), nameof(NextText), nameof(CanGoBack))]
    public partial int StepIndex { get; private set; }

    /// <summary>
    /// Whether the card shows (a tour started with <c>card=false</c> draws the dimming and the ring only,
    /// for the guide's pictures).
    /// </summary>
    [ObservableProperty]
    public partial bool ShowCard { get; private set; } = true;

    /// <summary>
    /// Whether what the step waits for holds (a step that asks the person to act).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWaiting), nameof(ShowsDone), nameof(NextText), nameof(HasDoItForMe))]
    public partial bool IsDone { get; private set; }

    /// <summary>
    /// Why a command of the step did not run, or why the target is not shown; null when all is well.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    public partial string? Problem { get; private set; }

    /// <summary>
    /// Where the step's target lies, set by the overlay.
    /// </summary>
    [ObservableProperty]
    public partial TourPlace Place { get; private set; } = TourPlace.Middle;

    /// <summary>
    /// Where the card lies in the window, set by the overlay; null while it does not show.
    /// </summary>
    [ObservableProperty]
    public partial Rect? CardBounds { get; private set; }

    public bool IsRunning => Running is not null;

    public bool HasProblem => Problem is not null;

    /// <summary>
    /// A step that asks the person to act and has not seen it done.
    /// </summary>
    public bool IsWaiting => Step is { Acts: true } && !IsDone;

    /// <summary>
    /// A step that asks the person to act and has seen it done: the card says what happened.
    /// </summary>
    public bool ShowsDone => Step is { Acts: true } && IsDone;

    public bool HasDoItForMe => Step is { DoItForMe: not null, Acts: true } && !IsDone;

    public bool CanGoBack => Running is not null && StepIndex > 0;

    public string StepCountText => Running is null ? "" : $"Step {StepIndex + 1} of {Running.Steps.Count}";

    public string StepTitle => Step?.Title ?? "";

    public string StepText => Step?.Text ?? "";

    /// <summary>
    /// What the step says while it waits for the person.
    /// </summary>
    public string WaitingText => "Waiting for you. Try it, or press Do it for me.";

    /// <summary>
    /// What the step says once it holds.
    /// </summary>
    public string DoneText => Step?.Done ?? "Done.";

    public string NextText => Running is null ? "Next" : StepIndex == 0 ? "Start" : StepIndex == Running.Steps.Count - 1 ? "Finish" : IsWaiting ? "Skip" : "Next";

    /// <summary>
    /// Sets where the target and the card lie (the overlay, after it placed them).
    /// </summary>
    public void SetPlace(TourPlace place, Rect? card)
    {
        Place = place;
        CardBounds = card;
        Problem = Problem is not null && Problem.StartsWith("A command", StringComparison.Ordinal) ? Problem
            : place.Found ? null : $"This part of the window is not shown now ({place.Why}).";
    }

    /// <summary>
    /// Starts a tour, at a step (from 0) when given; one that runs ends first.
    /// </summary>
    /// <param name="card">False draws the dimming and the ring without the card.</param>
    /// <exception cref="ArgumentException">There is no such tour, or its file cannot be used.</exception>
    /// <exception cref="InvalidOperationException">The tour needs an open map.</exception>
    public async Task StartAsync(string id, string? step = null, bool card = true)
    {
        TourEntry entry = Entries.FirstOrDefault(each => each.Tour?.Id == id || System.IO.Path.GetFileName(each.File) == id + TourScript.Suffix)
            ?? throw new ArgumentException($"There is no tour '{id}': {string.Join(", ", Entries.Where(each => each.IsOk).Select(each => each.Tour!.Id))}");
        Tour tour = entry.Tour ?? throw new ArgumentException($"The tour '{id}' cannot be used: {entry.Error}");
        if (tour.Needs.Contains("Map", StringComparer.OrdinalIgnoreCase) && !_model.HasMap)
        {
            throw new InvalidOperationException($"The tour '{id}' needs an open map");
        }
        int first = step is null ? 0 : int.TryParse(step, out int number) ? number - 1 : tour.Steps.ToList().FindIndex(each => each.Id == step);
        if (first < 0 || first >= tour.Steps.Count)
        {
            throw new ArgumentException($"The tour '{id}' has {tour.Steps.Count} steps ({string.Join(", ", tour.Steps.Select(each => each.Id))}): '{step}' is none of them");
        }
        await EndAsync();
        IsOpen = false;
        _offerDismissed = true;
        OnPropertyChanged(nameof(OfferShown));
        _restore = new TourRestore(_model.View, _model.SelectedMode?.Definition.Id, _model.ActiveTool?.Definition.Id);
        ShowCard = card;
        Running = tour;
        Log.LogInformation("The tour {Tour} starts at step {Step}", tour.Id, first + 1);
        foreach (TourAction action in tour.Start)
        {
            Problem = await RunActionAsync(action) is string why ? $"A command of the tour did not run: {why}" : null;
        }
        await GoToAsync(first);
    }

    /// <summary>
    /// Goes to the next step; at the last one the tour is finished.
    /// </summary>
    public async Task NextAsync()
    {
        if (Running is null)
        {
            throw new InvalidOperationException("No tour runs");
        }
        if (StepIndex >= Running.Steps.Count - 1)
        {
            await EndAsync(finished: true);
            return;
        }
        await GoToAsync(StepIndex + 1);
    }

    /// <summary>
    /// Goes back a step.
    /// </summary>
    public async Task BackAsync()
    {
        if (Running is null)
        {
            throw new InvalidOperationException("No tour runs");
        }
        if (StepIndex > 0)
        {
            await GoToAsync(StepIndex - 1);
        }
    }

    /// <summary>
    /// Runs what the step does instead of the person.
    /// </summary>
    /// <exception cref="InvalidOperationException">No tour runs, or the step has nothing to run.</exception>
    public async Task DoItAsync()
    {
        if (Running is null || Step is not { DoItForMe: { } action })
        {
            throw new InvalidOperationException(Running is null ? "No tour runs" : "This step has nothing to do for you");
        }
        Problem = await RunActionAsync(action) is string why ? $"A command of the step did not run: {why}" : null;
        await RefreshStageAsync();
        Evaluate();
    }

    /// <summary>
    /// Ends the tour that runs and puts back what it changed: the view, the mode and tool, the layout.
    /// </summary>
    /// <param name="finished">Whether the last step was reached: the tour is then kept as finished.</param>
    public async Task EndAsync(bool finished = false)
    {
        if (Running is not Tour tour)
        {
            return;
        }
        if (finished)
        {
            Progress.Finished[tour.Id] = DateTimeOffset.Now;
            WriteProgress();
            RefreshItems();
        }
        Log.LogInformation("The tour {Tour} ends at step {Step}{Finished}", tour.Id, StepIndex + 1, finished ? ", finished" : "");
        Running = null;
        Step = null;
        IsDone = false;
        Problem = null;
        CardBounds = null;
        Place = TourPlace.Middle;
        await RefreshStageAsync();
        TourRestore? restore = _restore;
        _restore = null;
        if (restore is not null)
        {
            await RestoreAsync(restore);
        }
        else
        {
            Ledger.Restore();
        }
        OnPropertyChanged(nameof(OfferShown));
    }

    private async Task GoToAsync(int index)
    {
        Tour tour = Running!;
        Step = tour.Steps[index];
        StepIndex = index;
        IsDone = false;
        Problem = null;
        foreach (TourAction action in Step.Before)
        {
            if (await RunActionAsync(action) is string why)
            {
                Problem = $"A command of the step did not run: {why}";
            }
        }
        OnPropertyChanged(nameof(DoneText));
        await RefreshStageAsync();
        Evaluate();
    }

    /// <summary>
    /// Has the overlay find the target and draw the step again.
    /// </summary>
    public async Task RefreshStageAsync()
    {
        if (Stage is { } stage)
        {
            await stage();
        }
    }

    /// <summary>
    /// Runs a command of a tour without a person; the reason it did not run, else null.
    /// </summary>
    private async Task<string?> RunActionAsync(TourAction action)
    {
        if (TourRefusals.Why(action.Run) is string refused)
        {
            Log.LogWarning("The tour {Tour} asked for {Command}, which tours may not run: {Reason}", Running?.Id, action.Run, refused);
            return $"{action.Run}: {refused}";
        }
        try
        {
            CommandRun run = await _model.Commands.TryRunAsync(action.Run, action.Arguments(), interactive: false);
            if (!run.Ran)
            {
                Log.LogWarning("The tour {Tour}'s command {Command} did not run: {Reason}", Running?.Id, action.Run, run.Why);
                return $"{action.Run}: {run.Why}";
            }
            return null;
        }
        catch (ArgumentException exception)
        {
            Log.LogWarning(exception, "The tour {Tour}'s command {Command} did not run: {Reason}", Running?.Id, action.Run, exception.Message);
            return $"{action.Run}: {exception.Message}";
        }
    }

    #endregion

    #region What a step waits for

    /// <summary>
    /// Looks at what the step waits for after something changed: at once when the last look is long ago,
    /// else once the 250 ms are over.
    /// </summary>
    private void Poke()
    {
        if (Step is not { Acts: true } || IsDone || _checkPending)
        {
            return;
        }
        TimeSpan wait = _lastCheck + ConditionInterval - _clock.Elapsed;
        if (wait <= TimeSpan.Zero)
        {
            Evaluate();
            return;
        }
        _checkPending = true;
        _ = Task.Delay(wait).ContinueWith(_ => UiThread.Run(_ui, () =>
        {
            _checkPending = false;
            Evaluate();
        }), TaskScheduler.Default);
    }

    /// <summary>
    /// Whether what the step waits for holds now (span <c>tour.condition</c>); latches <see cref="IsDone"/>.
    /// </summary>
    /// <returns>Whether the step is done.</returns>
    public bool Evaluate()
    {
        _lastCheck = _clock.Elapsed;
        if (Step is not { Until: { } until } || IsDone)
        {
            return IsDone;
        }
        using Performance.Span measure = Performance.Measure("tour.condition");
        if (TourState.Holds(_model, until))
        {
            IsDone = true;
            OnPropertyChanged(nameof(DoneText));
        }
        return IsDone;
    }

    #endregion

    #region Putting back

    private sealed record TourRestore(MapViewTab View, string? Mode, string? Tool);

    private async Task RestoreAsync(TourRestore restore)
    {
        try
        {
            if (_model.View != restore.View)
            {
                await RunAsync(restore.View switch { MapViewTab.Top => "view.top", MapViewTab.ThreeD => "view.3d", _ => "view.sideBySide" });
            }
            if (restore.Mode is string mode && _model.SelectedMode?.Definition.Id != mode)
            {
                await RunAsync($"{mode}.mode");
            }
            if (restore.Tool is string tool && _model.ActiveTool?.Definition.Id != tool)
            {
                await RunAsync($"tool.{tool}");
            }
        }
        finally
        {
            Ledger.Restore();
        }
    }

    private async Task RunAsync(string id)
    {
        CommandRun run = await _model.Commands.TryRunAsync(id, null, interactive: false);
        if (!run.Ran)
        {
            Log.LogWarning("The tour could not put {Command} back: {Reason}", id, run.Why);
        }
    }

    #endregion

    #region The catalogue

    /// <summary>
    /// Writes the editor's tours into a workspace's <c>tours</c> folder the first time, reads the folder and
    /// watches it: a tour file that changes is read again.
    /// </summary>
    public void Open(string folder)
    {
        try
        {
            EditorTours.Ensure(folder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.LogWarning(exception, "Could not write the editor's tours into {Folder}: {Reason}", folder, exception.Message);
        }
        Folder = Path.GetFullPath(folder);
        Progress = TourProgress.Read(Folder, (file, exception) => Log.LogWarning(exception, "Could not read the tours' progress {File}: {Reason}", file, exception.Message));
        _offerAnswered = Progress.Offered;
        Reload();
        Watch(Folder);
        ConsiderOffer();
    }

    /// <summary>
    /// Reads every tour file of the folder again; a file that cannot be used is listed with why and logged.
    /// </summary>
    public void Reload()
    {
        if (Folder is not string folder)
        {
            return;
        }
        List<TourEntry> entries = [];
        foreach (TourEntry entry in TourCatalog.Load(folder))
        {
            TourEntry checkedEntry = entry.Tour is { } tour && Unknown(tour) is string why ? entry with { Tour = null, Error = why } : entry;
            if (checkedEntry.Error is string error)
            {
                Log.LogWarning("The tour file {File} cannot be used: {Reason}", checkedEntry.File, error);
            }
            entries.Add(checkedEntry);
        }
        Entries = [.. entries.Where(each => each.IsOk), .. entries.Where(each => !each.IsOk)];
        RefreshItems();
        ConsiderOffer();
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>
    /// A command of the tour that the editor does not have; null when every one exists (a plugin's is not
    /// known before its plugin loads).
    /// </summary>
    private string? Unknown(Tour tour)
    {
        string[] unknown = [.. tour.Start.Concat(tour.Steps.SelectMany(step => step.Before)).Concat(tour.Steps.Select(step => step.DoItForMe).OfType<TourAction>())
            .Select(action => action.Run).Where(id => !id.StartsWith("plugin.", StringComparison.Ordinal) && _model.Commands.Find(id) is null).Distinct()];
        return unknown.Length == 0 ? null : $"the editor has no command {string.Join(", ", unknown)}";
    }

    private void RefreshItems()
    {
        Items.Clear();
        foreach (TourEntry entry in Entries)
        {
            DateTimeOffset? finished = entry.Tour is { } tour && Progress.Finished.TryGetValue(tour.Id, out DateTimeOffset when) ? when : null;
            string? id = entry.Tour?.Id;
            Items.Add(new TourListItem(entry, finished, new AsyncRelayCommand(() => id is null ? Task.CompletedTask : _model.TryAsync(() => StartAsync(id)))));
        }
    }

    private void Watch(string folder)
    {
        _watcher?.Dispose();
        _watcher = null;
        try
        {
            Directory.CreateDirectory(folder);
            _watcher = new ProjectWatcher(folder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Log.LogWarning(exception, "Cannot watch the tours in {Folder}: {Reason}", folder, exception.Message);
            return;
        }
        _watcher.Changed += files =>
        {
            if (files.Any(file => file.EndsWith(TourScript.Suffix, StringComparison.OrdinalIgnoreCase)))
            {
                UiThread.Run(_ui, Reload);
            }
        };
    }

    private void WriteProgress()
    {
        if (Folder is not string folder)
        {
            return;
        }
        try
        {
            Progress.Write(folder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.LogWarning(exception, "Could not write the tours' progress into {Folder}: {Reason}", folder, exception.Message);
        }
    }

    [RelayCommand]
    private void Close() => IsOpen = false;

    /// <summary>
    /// Opens the Tours page.
    /// </summary>
    public void OpenPage()
    {
        Reload();
        IsOpen = true;
    }

    #endregion

    #region The offer

    /// <summary>
    /// Whether the card that offers the window's tour shows: once per workspace, after the workspace is ready and
    /// a map is open, until the person answers. It does not block the editor.
    /// </summary>
    public bool OfferShown => Folder is not null && _model.HasMap && !_offerAnswered && !_offerDismissed && Running is null
        && Entries.Any(entry => entry.Tour?.Id == OfferedTour);

    private void ConsiderOffer() => OnPropertyChanged(nameof(OfferShown));

    /// <summary>
    /// The person's answer to the offer; Start and Don't ask again are remembered in the workspace.
    /// </summary>
    public async Task AnswerOfferAsync(TourOffer answer)
    {
        if (!OfferShown)
        {
            throw new InvalidOperationException("No tour is offered now");
        }
        if (answer == TourOffer.Later)
        {
            _offerDismissed = true;
            OnPropertyChanged(nameof(OfferShown));
            return;
        }
        Progress = Progress with { Offered = true };
        _offerAnswered = true;
        WriteProgress();
        OnPropertyChanged(nameof(OfferShown));
        if (answer == TourOffer.Start)
        {
            await StartAsync(OfferedTour);
        }
    }

    [RelayCommand]
    private Task AnswerOffer(string answer) => _model.TryAsync(() => AnswerOfferAsync(Enum.Parse<TourOffer>(answer, ignoreCase: true)));

    #endregion

    /// <summary>
    /// The state for automation: the tour that runs with where its target and card lie, the offer, the
    /// catalogue with the files that cannot be used.
    /// </summary>
    public JsonObject Describe() => new JsonObject
    {
        ["running"] = IsRunning,
        ["id"] = Running?.Id,
        ["step"] = Running is null ? null : StepIndex + 1,
        ["stepId"] = Step?.Id,
        ["of"] = Running?.Steps.Count,
        ["title"] = Step?.Title,
        ["text"] = Step?.Text,
        ["target"] = Running is null ? null : new JsonObject
        {
            ["named"] = Step?.Target?.ToString(),
            ["kind"] = Place.Kind,
            ["found"] = Place.Found,
            ["bounds"] = Place.Bounds is Rect bounds ? Rectangle(bounds) : null,
            ["why"] = Place.Why,
        },
        ["card"] = Running is null ? null : new JsonObject { ["shown"] = ShowCard, ["bounds"] = CardBounds is Rect card ? Rectangle(card) : null },
        ["acts"] = Step?.Acts,
        ["waiting"] = Running is null ? null : IsWaiting,
        ["done"] = Running is null ? null : IsDone,
        ["doItForMe"] = Running is null ? null : HasDoItForMe,
        ["problem"] = Problem,
        ["layoutChanged"] = new JsonArray([.. Ledger.Panels.Select(panel => (JsonNode)JsonValue.Create(panel))]),
        ["offer"] = new JsonObject { ["shown"] = OfferShown, ["answered"] = _offerAnswered },
        ["page"] = IsOpen,
        ["folder"] = Folder,
        ["tours"] = new JsonArray([.. Entries.Where(entry => entry.IsOk).Select(entry => (JsonNode)new JsonObject
        {
            ["id"] = entry.Tour!.Id,
            ["title"] = entry.Tour.Title,
            ["steps"] = entry.Tour.Steps.Count,
            ["finished"] = Progress.Finished.ContainsKey(entry.Tour.Id),
            ["file"] = System.IO.Path.GetFileName(entry.File),
        })]),
        ["errors"] = new JsonArray([.. Entries.Where(entry => !entry.IsOk).Select(entry => (JsonNode)new JsonObject
        {
            ["file"] = System.IO.Path.GetFileName(entry.File),
            ["error"] = entry.Error,
        })]),
    };

    private static JsonArray Rectangle(Rect rect) => [Math.Round(rect.X), Math.Round(rect.Y), Math.Round(rect.Width), Math.Round(rect.Height)];
}
