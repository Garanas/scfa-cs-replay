using System.Collections.Immutable;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Moves the selected things across the map (user story F-11), one step of undo, keeping each on
/// the terrain: what the arrow keys do in a view with the select tool (an ogrid, 8 with shift).
/// While the symmetry mirrors edits, each thing's mirror partners move by the image of the offset.
/// </summary>
/// <param name="Dx">In ogrids, to the right in the top view.</param>
/// <param name="Dz">In ogrids, down in the top view.</param>
[UndoStep]
[EditorCommand("edit.nudge", "Move the selection", Story = "F-11", Needs = Needs.Selection)]
public sealed record Nudge(float Dx, float Dz) : IRequest;

public sealed class NudgeHandler(MainViewModel model, SelectionViewModel selection, CurrentEdit edit) : IRequestHandler<Nudge>
{
    public Task Handle(Nudge request, CancellationToken cancellationToken)
    {
        ImmutableArray<ObjectRef> things = selection.Items;
        edit.Transaction.Apply(document => document.MoveObjects(things, model.PartnersOf(document, things), request.Dx, request.Dz));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Turns each selected thing about the up axis through its own centre (user story F-11), one step
/// of undo: what Q and E do in a view with the select tool (15 degrees, 90 with shift). A prop's
/// whole rotation turns, so a tilted rock stays tilted; areas do not turn. While the symmetry mirrors
/// edits, each thing's mirror partners turn with it (the other way in a mirrored part).
/// </summary>
/// <param name="Degrees">Clockwise in the top view; negative turns the other way. Left out, 15.</param>
[UndoStep]
[EditorCommand("edit.rotate", "Turn the selection", Story = "F-11", Needs = Needs.Selection)]
public sealed record Rotate(float Degrees = 15) : IRequest;

public sealed class RotateHandler(MainViewModel model, SelectionViewModel selection, CurrentEdit edit) : IRequestHandler<Rotate>
{
    public Task Handle(Rotate request, CancellationToken cancellationToken)
    {
        ImmutableArray<ObjectRef> things = selection.Items;
        edit.Transaction.Apply(document => document.RotateObjects(things, model.PartnersOf(document, things), request.Degrees * MathF.PI / 180));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Puts each selected thing's centre at a place and turns it to face a way (user story F-11), one
/// step of undo: what the inspector's fields do. What is left out stays, so <c>x</c> alone lines
/// the selection up; the height follows the terrain. While the symmetry mirrors edits, each thing's
/// mirror partners go to the image of its new place and turn as much.
/// </summary>
/// <param name="X">In ogrids.</param>
/// <param name="Heading">In degrees, clockwise from +x in the top view.</param>
[UndoStep]
[EditorCommand("edit.place", "Place the selection", Story = "F-11", Needs = Needs.Selection)]
public sealed record PlaceSelection(float? X = null, float? Z = null, float? Heading = null) : IRequest;

public sealed class PlaceSelectionHandler(MainViewModel model, SelectionViewModel selection, CurrentEdit edit) : IRequestHandler<PlaceSelection>
{
    public Task Handle(PlaceSelection request, CancellationToken cancellationToken)
    {
        ImmutableArray<ObjectRef> things = selection.Items;
        edit.Transaction.Apply(document => document.PlaceObjects(things, model.PartnersOf(document, things), request.X, request.Z,
            request.Heading is float heading ? heading * MathF.PI / 180 : null));
        return Task.CompletedTask;
    }
}
