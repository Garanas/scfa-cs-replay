using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// What the editor generates when asked (user story F-7): the generators of the features
/// (walkability, the normal map and preview, props from rules, ...), their last results for the
/// open map, and which of those are out of date. Nothing generates by itself; the status bar says
/// how many results are out of date, and Generate, Everything out of date makes them again.
/// </summary>
public sealed partial class GenerationViewModel : ObservableObject
{
    /// <summary>
    /// The generators and their results; a feature registers its generator here.
    /// </summary>
    public GeneratedResults Results { get; } = new GeneratedResults();

    /// <summary>
    /// How every result stands against the open map, as last refreshed.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Stale), nameof(HasStale), nameof(Summary))]
    public partial IReadOnlyList<GeneratedState> States { get; private set; } = [];

    /// <summary>
    /// The results whose inputs changed since they were generated.
    /// </summary>
    public IReadOnlyList<GeneratedState> Stale => [.. States.Where(state => state.Status == GeneratedStatus.Stale)];

    public bool HasStale => Stale.Count > 0;

    /// <summary>
    /// For the status bar, e.g. <c>Out of date: Walkability</c>; empty when nothing is.
    /// </summary>
    public string Summary => HasStale ? $"Out of date: {string.Join(", ", Stale.Select(state => state.Generator.Title))}" : "";

    /// <summary>
    /// Compares the results with the document as it is now: after every step, not every dab.
    /// </summary>
    public void Refresh(MapDocument? document)
    {
        using Performance.Span measure = Performance.Measure("model.generated");
        States = document is null ? [] : Results.States(document);
    }

    /// <summary>
    /// Forgets the results: another map is open.
    /// </summary>
    public void Forget()
    {
        Results.Forget();
        States = [];
    }

    /// <summary>
    /// Generates every result that is out of date from <paramref name="document"/>.
    /// </summary>
    /// <returns>What was out of date, and why.</returns>
    public IReadOnlyList<GeneratedState> GenerateStale(MapDocument document)
    {
        IReadOnlyList<GeneratedState> stale = Results.Stale(document);
        foreach (GeneratedState state in stale)
        {
            using Performance.Span measure = Performance.Measure("model.generate");
            Results.Generate(state.Generator.Id, document);
        }
        Refresh(document);
        return stale;
    }
}
