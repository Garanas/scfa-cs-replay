using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.Rendering;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Checks;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;
using FAForever.MapEditor.Project.Workspaces;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// What the core generates and checks (user stories T-7, S-4, F-14): the generators of walkability,
/// the normal map and the preview, generating one on a thread of the pool, and the problems of the
/// map with going to one.
/// </summary>
public sealed partial class MainViewModel
{
    private readonly ConcurrentDictionary<string, bool> _hasAsset = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
    private AssetCatalog? _checkedCatalog;

    /// <summary>
    /// The last walkability of the map and what changed since the one before (user story T-7).
    /// </summary>
    public WalkabilityViewModel Walkability { get; private set; } = null!;

    /// <summary>
    /// What is wrong or doubtful about the map: the Problems tab (user story F-14).
    /// </summary>
    public ProblemsViewModel Problems { get; private set; } = null!;

    /// <summary>
    /// Whether the views show where units of the chosen layer can go, and what changed (user story T-7).
    /// </summary>
    [ObservableProperty]
    public partial bool ShowWalkability { get; set; }

    partial void OnShowWalkabilityChanged(bool value)
    {
        // one tint over the terrain at a time: walkability's red or a mask's violet
        if (value)
        {
            ShowMask = false;
        }
        RefreshOverlays();
    }

    private void InitializeGeneration()
    {
        Generation.Register(new WalkabilityGenerator(), "generate.walkability");
        Generation.Register(new NormalMapGenerator(), "generate.normalMap");
        Generation.Register(new MapPreview(() => Setup.Workspace), "file.renderPreview");
        Walkability = new WalkabilityViewModel(Generation);
        Walkability.Changed += (_, _) =>
        {
            RefreshOverlays();
            Problems.Schedule();
        };

        Problems = new ProblemsViewModel(CheckContext, _ui, Logger("Editor"));
        Events.MapOpened += _ => Problems.Run();
        Events.DocumentChanged += _ => Problems.Schedule();
        Events.BeforeSave += (_, _) => CheckBefore("Saving");
        Events.BeforeExport += (_, _) => CheckBefore("Exporting");
        Events.MapSaved += _ => MentionProblems();
        Events.MapExported += (_, _) => MentionProblems();
        Assets.PropertyChanged += OnCatalogChanged;
    }

    /// <summary>
    /// What the checks read now: the map, the workspace's assets, the last walkability's changes,
    /// and the fingerprints of the workspace's unit presets (kept by the presets part).
    /// </summary>
    private ProblemContext? CheckContext() => Map?.Document is MapDocument document
        ? new ProblemContext(
            document,
            Assets.Catalog.Entries.Count > 0 ? Assets.Catalog : null,
            Setup.Workspace is Workspace workspace ? path => _hasAsset.GetOrAdd(path, key => workspace.FindAsset(key) is not null) : null,
            Generation.Results,
            Walkability.LandChanges,
            Presets.HasLibrary ? Presets.UnitPresetFingerprint : null)
        : null;

    // the assets were read again: the checks look at them again
    private void OnCatalogChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AssetsViewModel.Kinds) && !ReferenceEquals(Assets.Catalog, _checkedCatalog))
        {
            _checkedCatalog = Assets.Catalog;
            _hasAsset.Clear();
            Problems.Schedule();
        }
    }

    /// <summary>
    /// Runs the checks before a save or export. Problems never stop it: a map maker saves work in
    /// progress, and the Problems tab and the status line say what is wrong.
    /// </summary>
    private void CheckBefore(string operation)
    {
        Problems.Run();
        if (Problems.Errors > 0)
        {
            Logger("Editor").LogWarning("{Operation} a map with problems: {Summary}", operation, Problems.Summary);
        }
    }

    private void MentionProblems()
    {
        if (Problems.Errors + Problems.Warnings > 0)
        {
            Status = $"{Status} ({Problems.Summary}: see Problems)";
        }
    }

    // the result a derived change is putting into the map, kept again for the version it makes
    private (IGenerator Generator, object Value)? _deriving;

    /// <summary>
    /// Puts a generated result that is part of the map (<see cref="IDocumentGenerator"/>) into the
    /// open map's current version without a step of undo (the owner's decision: generated results
    /// are derived, the sources they are made from have the steps): the library's
    /// <see cref="EditSession.Derive"/>, which waits while a stroke holds its transaction. A result
    /// that was current for the version it goes into is kept again for the version it makes, so
    /// putting it in place never makes it out of date nor starts it again on its own.
    /// </summary>
    internal void DeriveResult(EditedMap map, IDocumentGenerator generator, object value)
    {
        map.Session.Derive($"Generate the {generator.Title.ToLowerInvariant()}", now =>
        {
            GeneratedResult? kept = Generation.Results.ResultOf(generator.Id);
            _deriving = kept is not null && ReferenceEquals(kept.Value, value) && !kept.IsStale(now) ? (generator, value) : null;
            return generator.Apply(now, value);
        });
        if (!map.Session.InTransaction)
        {
            _deriving = null;
        }
    }

    /// <summary>
    /// After a derived change: its result as the result of the version it made (<see cref="DeriveResult"/>).
    /// After an undo or redo: for every generator, a result remembered for the version gone back or
    /// forth to (<see cref="GeneratedResults.Recall"/>: the last 8 of each, by the versions of their
    /// inputs) is kept again, so the views show the result made for that version at once and nothing
    /// regenerates: walkability, the connected regions and the path networks beside the map as much as
    /// the results the version holds (the owner's decision Q4).
    /// </summary>
    private void KeepDerived(DocumentChange change)
    {
        if (change.Reason == ChangeReason.Edit)
        {
            return;
        }
        if (change.Reason == ChangeReason.Derive)
        {
            if (_deriving is (IGenerator generator, object value))
            {
                _deriving = null;
                Generation.Results.Keep(GeneratedResult.Of(generator, change.Current, value));
            }
            return;
        }
        using Performance.Span measure = Performance.Measure("model.generate-recall");
        if (Generation.Results.Recall(change.Current).Count > 0)
        {
            Generation.Refresh(change.Current);
        }
    }

    /// <summary>
    /// Generates one result from the map as it is, on a thread of the pool (the editing goes on
    /// meanwhile, and a result of a version edited since shows as out of date). A result that is
    /// part of the map (<see cref="IDocumentGenerator"/>: the normal map, the preview) goes into its
    /// current version without a step of undo (<see cref="DeriveResult"/>).
    /// </summary>
    /// <exception cref="ArgumentException">There is no such generator.</exception>
    /// <exception cref="InvalidOperationException">No map is open, or it is being generated already.</exception>
    public async Task<GeneratedResult> GenerateAsync(string id)
    {
        EditedMap map = RequireMap();
        IGenerator generator = Generation.Results.Find(id) ?? throw new ArgumentException($"There is no generator '{id}'");
        // a person's Generate takes over from a run regenerating on its own
        AutoGeneration?.Stop(id);
        if (!Generation.SetRunning(id, true))
        {
            throw new InvalidOperationException($"The {generator.Title.ToLowerInvariant()} is being generated already");
        }
        ILogger log = Logger("Editor");
        MapDocument document = map.Document;
        long start = Stopwatch.GetTimestamp();
        Generation.Results.HandResults(generator);
        try
        {
            object value = await Task.Run(() =>
            {
                using Performance.Span measure = Performance.Measure("model.generate");
                return generator.Generate(document);
            });
            GeneratedResult result = GeneratedResult.Of(generator, document, value);
            double milliseconds = Math.Round(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            EditorMetrics.OperationDuration.Record(milliseconds, EditorMetrics.Tag("operation", "generate"), EditorMetrics.Tag("result", "ok"));
            log.LogInformation("Generated the {Generator} ({Milliseconds} ms)", generator.Id, milliseconds);
            if (Map != map)
            {
                // another map was opened meanwhile: the result is of no map shown
                return result;
            }
            Generation.Results.Keep(result);
            Generation.NoteMade(id, Stopwatch.GetElapsedTime(start), GenerationWay.Asked);
            if (generator is IDocumentGenerator part)
            {
                DeriveResult(map, part, value);
            }
            return result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            double milliseconds = Math.Round(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            EditorMetrics.OperationDuration.Record(milliseconds, EditorMetrics.Tag("operation", "generate"), EditorMetrics.Tag("result", "failed"));
            log.LogError(exception, "Generating the {Generator} failed: {Reason}", generator.Id, exception.Message);
            throw;
        }
        finally
        {
            Generation.SetRunning(id, false);
            Generation.Refresh(Map?.Document);
        }
    }

    /// <summary>
    /// Generates every result that is out of date (<c>generate.stale</c>) in the order in which one
    /// feeds the next (masks from rules before props from rules, walkability before path networks);
    /// a result that is part of the map goes into it without a step of undo, and what it makes out of
    /// date is generated after it.
    /// </summary>
    public IReadOnlyList<GeneratedState> GenerateStale() => GenerateStale(missing: false, out _);

    /// <summary>
    /// Generates what is out of date and, with <paramref name="missing"/>, what was never made for
    /// the map (<c>generate.all</c>), each put into the map without a step of undo when it is part of it.
    /// </summary>
    /// <param name="left">The results never made that were left out, with why.</param>
    public IReadOnlyList<GeneratedState> GenerateStale(bool missing, out IReadOnlyList<(IGenerator Generator, string Reason)> left)
    {
        EditedMap map = RequireMap();
        return Generation.GenerateStale(map.Document, (generator, value) =>
        {
            DeriveResult(map, generator, value);
            return map.Document;
        }, missing, out left);
    }

    /// <summary>
    /// Goes to a problem of the list: selects its thing, and the top view and the 3D camera look at
    /// its place (the thing's when it names none).
    /// </summary>
    /// <returns>Whether it led anywhere.</returns>
    /// <exception cref="ArgumentException">The list has no such line.</exception>
    public bool ShowProblem(int index)
    {
        if (index < 0 || index >= Problems.Items.Count)
        {
            throw new ArgumentException($"There is no problem {index}: the list has {Problems.Items.Count}");
        }
        Problem problem = Problems.Items[index].Problem;
        Status = problem.Message;
        if (Map?.Document is not MapDocument document)
        {
            return false;
        }
        Vector2? place = problem.Place;
        if (problem.Thing is ObjectRef thing && document.Exists(thing))
        {
            Selection.Set([thing]);
            if (place is null && document.PlaceOf(thing) is Vector3 at)
            {
                place = new Vector2(at.X, at.Z);
            }
        }
        if (place is not Vector2 shown)
        {
            return problem.Thing is not null;
        }
        RequestTopView(shown.X, shown.Y, ProblemZoom);
        FocusCamera(shown.X, shown.Y, ProblemRadius);
        return true;
    }

    /// <summary>
    /// How close the top view shows a problem's place: pixels an ogrid.
    /// </summary>
    public const double ProblemZoom = 4;

    /// <summary>
    /// How much around a problem's place the 3D camera shows, in ogrids.
    /// </summary>
    public const float ProblemRadius = 30;
}
