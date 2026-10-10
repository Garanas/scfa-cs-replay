using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Selects every thing like the selected ones (user story F-11), of the kinds that can be picked
/// now: props and units of the same blueprint (with <c>family</c>, of the same family, such as
/// every pine or every tech 1 power generator of the faction), decals of the same texture, markers
/// of the same kind (deposits of the same resource, path nodes of the same layer).
/// </summary>
/// <param name="Family">Props and units of the same family of the workspace's assets, not only
/// the same blueprint.</param>
/// <returns>How many things are selected.</returns>
[EditorCommand("select.similar", "Select similar", Gesture = "Ctrl+Shift+G", Story = "F-11", Needs = Needs.Selection)]
public sealed record SelectSimilar(bool Family = false) : IRequest<int>;

public sealed class SelectSimilarHandler(MainViewModel model) : IRequestHandler<SelectSimilar, int>
{
    public Task<int> Handle(SelectSimilar request, CancellationToken cancellationToken)
    {
        int count = model.SelectSimilar(request.Family);
        model.Status = $"Selected {count} similar thing{(count == 1 ? "" : "s")}";
        return Task.FromResult(count);
    }
}

/// <summary>
/// Selects what can be picked now and is not selected (user story F-11), and takes the selected
/// things out: of the kinds a box takes (areas only in the Map mode).
/// </summary>
/// <returns>How many things are selected.</returns>
[EditorCommand("select.invert", "Invert the selection", Gesture = "Ctrl+Shift+I", Story = "F-11", Needs = Needs.Map)]
public sealed record InvertSelection : IRequest<int>;

public sealed class InvertSelectionHandler(MainViewModel model) : IRequestHandler<InvertSelection, int>
{
    public Task<int> Handle(InvertSelection request, CancellationToken cancellationToken)
    {
        model.InvertSelection();
        return Task.FromResult(model.Selection.Count);
    }
}

/// <summary>
/// Selects what lies inside an area (user story F-11), of the kinds a box takes: the area
/// selected, else the one named, else the playable area.
/// </summary>
/// <param name="Area">The area's name; left out, the selected area, else the playable area.</param>
/// <param name="Add">Adds to the selection in place of replacing it.</param>
/// <returns>How many things are selected.</returns>
[EditorCommand("select.insideArea", "Select inside an area", Story = "F-11", Needs = Needs.Map)]
public sealed record SelectInsideArea(string? Area = null, bool Add = false) : IRequest<int>;

public sealed class SelectInsideAreaHandler(MainViewModel model, SelectionViewModel selection) : IRequestHandler<SelectInsideArea, int>
{
    public Task<int> Handle(SelectInsideArea request, CancellationToken cancellationToken)
    {
        string area = request.Area
            ?? selection.Items.Where(thing => thing.Kind == ObjectKind.Area).Select(thing => thing.Name).FirstOrDefault()
            ?? model.ShownDocument?.PlayableArea?.Name
            ?? throw new ArgumentException("The map has no areas: give one with area=AREA_1 after adding it in the Map mode");
        model.SelectInsideArea(area, request.Add ? SelectionChange.Add : SelectionChange.Replace);
        model.Status = $"Selected {selection.Count} thing{(selection.Count == 1 ? "" : "s")} inside {area}";
        return Task.FromResult(selection.Count);
    }
}
