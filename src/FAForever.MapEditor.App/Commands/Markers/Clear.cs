using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Removes every marker of a kind (user story R-6), one step of undo: the Clear buttons of the
/// Markers mode's inspector. Links of path nodes and places in chains go with them, a spawn takes its
/// playable army (<see cref="MarkerEdits.RemoveKind"/>). Only the map's own markers go: the
/// Procedural mode's generated path networks are never markers of the project, so clearing a layer
/// makes the generated network its only one.
/// </summary>
/// <param name="Kind"><c>spawns</c>, <c>mass</c>, <c>hydrocarbon</c>, <c>ai</c>, <c>weather</c> or
/// <c>path</c> (a name of <c>markers.showKind</c> such as <c>landPaths</c> works too).</param>
/// <param name="Layer">With <c>path</c>: <c>land</c>, <c>water</c>, <c>amphibious</c> or <c>air</c>;
/// left out, the path nodes of every layer.</param>
/// <returns>How many markers went.</returns>
[UndoStep]
[EditorCommand("markers.clear", "Clear the markers of a kind", Story = "R-6", Needs = Needs.Map)]
public sealed record ClearMarkers(string Kind, string? Layer = null) : IRequest<int>;

public sealed class ClearMarkersHandler(MainViewModel model, CurrentEdit edit) : IRequestHandler<ClearMarkers, int>
{
    public Task<int> Handle(ClearMarkers request, CancellationToken cancellationToken)
    {
        MarkerKinds kinds = MarkersViewModel.KindsOf(request.Kind, request.Layer);
        MapDocument changed;
        int removed;
        using (Performance.Measure("model.markers-clear"))
        {
            changed = edit.Transaction.Document.RemoveKind(kinds, out removed);
        }
        if (removed == 0)
        {
            model.Status = $"The map has no {MarkersViewModel.Noun(kinds, 2).Split(' ', 2)[1]} to remove";
            return Task.FromResult(0);
        }
        string what = MarkersViewModel.Noun(kinds, removed, number: true);
        edit.Transaction.Apply(_ => changed);
        edit.Transaction.Description = $"Remove {what}";
        model.Status = $"Removed {what}";
        model.Logger("Markers").LogInformation("Removed {Count} markers of the kinds {Kinds}", removed, kinds);
        return Task.FromResult(removed);
    }
}

/// <summary>
/// Selects every marker of a kind (user story R-6): the Select them buttons of the Markers mode's
/// inspector. The kind shows in the views, with its overlay, so the selection can be seen.
/// </summary>
/// <param name="Kind">As <c>markers.clear</c> takes it.</param>
/// <param name="Layer">As <c>markers.clear</c> takes it.</param>
/// <returns>How many markers are selected.</returns>
[EditorCommand("markers.select", "Select the markers of a kind", Story = "R-6", Needs = Needs.Map)]
public sealed record SelectMarkers(string Kind, string? Layer = null) : IRequest<int>;

public sealed class SelectMarkersHandler(MainViewModel model, SelectionViewModel selection) : IRequestHandler<SelectMarkers, int>
{
    public Task<int> Handle(SelectMarkers request, CancellationToken cancellationToken)
    {
        MarkerKinds kinds = MarkersViewModel.KindsOf(request.Kind, request.Layer);
        MapDocument document = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
        ObjectRef[] things = [.. document.Markers.All.Where(marker => (MarkerEdits.KindOf(marker) & kinds) != 0).Select(marker => ObjectRef.Marker(marker.Id))];
        model.Markers.Show(kinds, true);
        if ((kinds & MarkerKinds.PathNodes) != 0)
        {
            model.ShowPathNodes = true;
        }
        if ((kinds & ~MarkerKinds.PathNodes) != 0)
        {
            model.ShowMarkers = true;
        }
        selection.Apply(things, SelectionChange.Replace);
        model.Status = things.Length == 0 ? $"Selected nothing: the map has no {MarkersViewModel.Noun(kinds, 2).Split(' ', 2)[1]}" : $"Selected {MarkersViewModel.Noun(kinds, things.Length, number: true)}";
        return Task.FromResult(things.Length);
    }
}
