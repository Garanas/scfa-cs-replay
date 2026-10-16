using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.Project.Generation;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// Walkability (user story T-7): the last generated navigational mesh of the open map, the one
/// before it, and what changed between them in the layer the views show. The result itself lives
/// in the generated results (<see cref="GenerationViewModel"/>); this part notices when another one
/// is kept, by whichever command, and compares it with the one it replaced.
/// </summary>
public sealed partial class WalkabilityViewModel : ObservableObject
{
    private readonly GenerationViewModel _generation;
    private GeneratedResult? _current;
    private GeneratedResult? _previous;

    public WalkabilityViewModel(GenerationViewModel generation)
    {
        _generation = generation;
        generation.PropertyChanged += OnGenerationChanged;
    }

    /// <summary>
    /// The layers one can show, in the order of the panel's choice.
    /// </summary>
    public IReadOnlyList<NavLayer> Layers { get; } = [NavLayer.Land, NavLayer.Hover, NavLayer.Amphibious, NavLayer.Water];

    /// <summary>
    /// The layer the views show and compare: land unless chosen.
    /// </summary>
    [ObservableProperty]
    public partial NavLayer Layer { get; set; } = NavLayer.Land;

    /// <summary>
    /// Whether the views show what changed since the generation before, over the walkability.
    /// </summary>
    [ObservableProperty]
    public partial bool ShowChanges { get; set; } = true;

    /// <summary>
    /// The last generated mesh, or null.
    /// </summary>
    public MapNavigation? Navigation => _current?.Value as MapNavigation;

    /// <summary>
    /// The shown layer of <see cref="Navigation"/>.
    /// </summary>
    public NavGrid? Grid => Navigation?[Layer];

    /// <summary>
    /// What changed in the shown layer between the last two generations; null before the second.
    /// </summary>
    public WalkabilityChanges? Changes { get; private set; }

    /// <summary>
    /// What changed in the land layer, for the checks (user story F-14): a new passage, a closed path.
    /// </summary>
    public WalkabilityChanges? LandChanges { get; private set; }

    /// <summary>
    /// For the panel and the status line, e.g. <c>Land: 12 ogrids blocked, 30 opened; 1 new passage</c>.
    /// </summary>
    public string ChangesText => Navigation is null
        ? "Not generated yet: generate it to see where units can go."
        : Changes is not { } changes
            ? $"{Layer}: {Passable:N0} of {Grid!.Cells.Length:N0} ogrids passable. Generate again after a change to see what changed."
            : Describe(changes);

    /// <summary>
    /// Raised when another result is kept or the shown layer changes: the views draw it again.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// What changed, in words.
    /// </summary>
    public static string Describe(WalkabilityChanges changes)
    {
        if (changes.IsEmpty)
        {
            return $"{changes.Layer}: nothing changed since the last time.";
        }
        List<string> counts = [];
        if (changes.Blocked > 0)
        {
            counts.Add($"{changes.Blocked:N0} ogrids newly blocked");
        }
        if (changes.Opened > 0)
        {
            counts.Add($"{changes.Opened:N0} {(changes.Blocked > 0 ? "" : "ogrids ")}opened");
        }
        List<string> parts = [$"{changes.Layer}: {string.Join(", ", counts)}"];
        if (changes.Passages.Count > 0)
        {
            parts.Add(changes.Passages.Count == 1 ? "1 new passage" : $"{changes.Passages.Count} new passages");
        }
        if (changes.Closures.Count > 0)
        {
            parts.Add(changes.Closures.Count == 1 ? "1 path closed" : $"{changes.Closures.Count} paths closed");
        }
        return string.Join("; ", parts);
    }

    /// <summary>
    /// How many ogrids of the shown layer units can go over; counted once per result and layer.
    /// </summary>
    public int Passable { get; private set; }

    partial void OnLayerChanged(NavLayer value) => Compare();

    partial void OnShowChangesChanged(bool value) => Changed?.Invoke(this, EventArgs.Empty);

    private void OnGenerationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(GenerationViewModel.States))
        {
            return;
        }
        GeneratedResult? kept = _generation.Results.ResultOf(WalkabilityGenerator.GeneratorId);
        if (ReferenceEquals(kept, _current))
        {
            return;
        }
        // another map forgets the results: nothing to compare with either
        _previous = kept is null ? null : _current;
        _current = kept;
        Compare();
    }

    private void Compare()
    {
        using Performance.Span measure = Performance.Measure("model.walkability-changes");
        MapNavigation? before = _previous?.Value as MapNavigation;
        MapNavigation? after = Navigation;
        Changes = before is null || after is null ? null : WalkabilityChanges.Compare(before, after, Layer);
        Passable = after?[Layer].Cells.Count(cell => cell > 0) ?? 0;
        LandChanges = before is null || after is null ? null : Layer == NavLayer.Land ? Changes : WalkabilityChanges.Compare(before, after, NavLayer.Land);
        OnPropertyChanged(nameof(Navigation));
        OnPropertyChanged(nameof(Grid));
        OnPropertyChanged(nameof(Changes));
        OnPropertyChanged(nameof(LandChanges));
        OnPropertyChanged(nameof(Passable));
        OnPropertyChanged(nameof(ChangesText));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
