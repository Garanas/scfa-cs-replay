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
/// makes the generated network its only one. Clearing spawns that take armies with them asks first
/// (the status line names the armies that went); the other kinds clear without a question.
/// </summary>
/// <param name="Kind"><c>spawns</c>, <c>mass</c>, <c>hydrocarbon</c>, <c>ai</c>, <c>blank</c>, <c>weather</c> or
/// <c>path</c> (a name of <c>markers.showKind</c> such as <c>landPaths</c> works too).</param>
/// <param name="Layer">With <c>path</c>: <c>land</c>, <c>water</c>, <c>amphibious</c> or <c>air</c>;
/// left out, the path nodes of every layer.</param>
/// <param name="Confirm">Yes given beforehand to removing the armies that go with their spawns; without a
/// person, clearing spawns that take armies needs it.</param>
/// <returns>How many markers went; 0 when the question was answered no.</returns>
[UndoStep]
[EditorCommand("markers.clear", "Clear the markers of a kind", Story = "R-6", Needs = Needs.Map)]
public sealed record ClearMarkers(string Kind, string? Layer = null, bool Confirm = false) : IRequest<int>;

public sealed class ClearMarkersHandler(MainViewModel model, CurrentEdit edit, IEditorDialogs dialogs) : IRequestHandler<ClearMarkers, int>
{
    public async Task<int> Handle(ClearMarkers request, CancellationToken cancellationToken)
    {
        MarkerKinds kinds = MarkersViewModel.KindsOf(request.Kind, request.Layer);
        MapDocument document = edit.Transaction.Document;
        MapDocument changed;
        int removed;
        using (Performance.Measure("model.markers-clear"))
        {
            changed = document.RemoveKind(kinds, out removed);
        }
        if (removed == 0)
        {
            model.Status = $"The map has no {MarkersViewModel.Noun(kinds, 2).Split(' ', 2)[1]} to remove";
            return 0;
        }
        string what = MarkersViewModel.Noun(kinds, removed, number: true);
        string[] armies = RemovedArmies(document, changed);
        if (armies.Length > 0 && !request.Confirm
            && !await dialogs.ConfirmAsync("Clear the spawns",
                $"Removing {what} also removes {Armies(armies)}, which {(armies.Length == 1 ? "has" : "have")} no units. One step of undo brings {(armies.Length == 1 ? "it" : "them")} back.",
                "Remove them"))
        {
            model.Status = $"Kept the {MarkersViewModel.Noun(kinds, 2).Split(' ', 2)[1]}";
            return 0;
        }
        edit.Transaction.Apply(_ => changed);
        edit.Transaction.Description = $"Remove {what}";
        model.Status = armies.Length == 0 ? $"Removed {what}" : $"Removed {what} and {Armies(armies)}";
        model.Logger("Markers").LogInformation("Removed {Count} markers of the kinds {Kinds} and the armies {Armies}", removed, kinds, armies);
        return removed;
    }

    /// <summary>
    /// The armies the map had before and has no longer, in the map's order.
    /// </summary>
    internal static string[] RemovedArmies(MapDocument before, MapDocument after) =>
        before.Armies == after.Armies ? [] : [.. before.Armies.Select(army => army.Name).Where(name => after.FindArmy(name) is null)];

    /// <summary>
    /// <c>the army ARMY_1</c>, <c>the armies ARMY_1 and ARMY_2</c>.
    /// </summary>
    internal static string Armies(IReadOnlyList<string> names) => names.Count == 1
        ? $"the army {names[0]}"
        : $"the armies {string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}";
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
