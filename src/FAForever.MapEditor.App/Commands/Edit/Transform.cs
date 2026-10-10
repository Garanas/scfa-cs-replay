using System.Collections.Immutable;
using System.Numerics;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Moves the selected things across the map (user story F-11), one step of undo, keeping each on
/// the terrain: what the arrow keys do in a view with the select tool (an ogrid, 8 with shift).
/// </summary>
/// <param name="Dx">In ogrids, to the right in the top view.</param>
/// <param name="Dz">In ogrids, down in the top view.</param>
[UndoStep]
[EditorCommand("edit.nudge", "Move the selection", Story = "F-11", Needs = Needs.Selection)]
public sealed record Nudge(float Dx, float Dz) : IRequest;

public sealed class NudgeHandler(SelectionViewModel selection, CurrentEdit edit) : IRequestHandler<Nudge>
{
    public Task Handle(Nudge request, CancellationToken cancellationToken)
    {
        ImmutableArray<ObjectRef> things = selection.Items;
        edit.Transaction.Apply(document => document.MoveObjects(things, request.Dx, request.Dz));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Turns the selection about the up axis (user story F-11), one step of undo: what Q and E do in
/// a view with the select tool (15 degrees, 90 with shift). About the selection's centre the things
/// go round it and each turns too; about each centre they turn where they are (the handles'
/// pivot, <see cref="HandleSettings.Pivot"/>, unless given). A prop's whole rotation turns, so a
/// tilted rock stays tilted; areas do not turn.
/// </summary>
/// <param name="Degrees">Clockwise in the top view; negative turns the other way. Left out, 15.</param>
/// <param name="About"><c>selection</c> or <c>each</c>; left out, the handles' pivot.</param>
[UndoStep]
[EditorCommand("edit.rotate", "Turn the selection", Story = "F-11", Needs = Needs.Selection)]
public sealed record Rotate(float Degrees = 15, string? About = null) : IRequest;

public sealed class RotateHandler(MainViewModel model, SelectionViewModel selection, CurrentEdit edit) : IRequestHandler<Rotate>
{
    public Task Handle(Rotate request, CancellationToken cancellationToken)
    {
        ImmutableArray<ObjectRef> things = selection.Items;
        float radians = request.Degrees * MathF.PI / 180;
        HandlePivot pivot = request.About is string about ? SetHandlesHandler.ParsePivot(about) : model.Handles.Pivot;
        edit.Transaction.Apply(document => pivot == HandlePivot.Selection && things.Length > 1 && SelectionFrame.Of(document, things) is SelectionFrame frame
            ? document.RotateObjectsAbout(things, radians, frame.Middle)
            : document.RotateObjects(things, radians));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Scales the selection (user story F-11), one step of undo: what the scale handles do. Props
/// scale alike along every axis, decals by their width and length, areas by their sides; about
/// the selection's centre the things spread from it (markers and units too), about each centre
/// they scale where they are.
/// </summary>
/// <param name="Factor">Both sides alike; left out, 1.</param>
/// <param name="X">The width (along the selection's box) on top of the factor.</param>
/// <param name="Z">The length on top of the factor.</param>
/// <param name="About"><c>selection</c> or <c>each</c>; left out, the handles' pivot.</param>
[UndoStep]
[EditorCommand("edit.scale", "Scale the selection", Story = "F-11", Needs = Needs.Selection)]
public sealed record ScaleSelection(float? Factor = null, float? X = null, float? Z = null, string? About = null) : IRequest;

public sealed class ScaleSelectionHandler(MainViewModel model, SelectionViewModel selection, CurrentEdit edit) : IRequestHandler<ScaleSelection>
{
    public Task Handle(ScaleSelection request, CancellationToken cancellationToken)
    {
        float factor = request.Factor ?? 1;
        float x = factor * (request.X ?? 1);
        float z = factor * (request.Z ?? 1);
        if (!(x > 0) || !(z > 0) || !float.IsFinite(x) || !float.IsFinite(z))
        {
            throw new ArgumentException("A scale is above 0: factor=1.5, or x=2 for the width only");
        }
        ImmutableArray<ObjectRef> things = selection.Items;
        HandlePivot pivot = request.About is string about ? SetHandlesHandler.ParsePivot(about) : model.Handles.Pivot;
        edit.Transaction.Apply(document => SelectionFrame.Of(document, things) is SelectionFrame frame
            ? document.ScaleObjects(things, x, z, frame.Heading, pivot == HandlePivot.Selection ? frame.Middle : null)
            : document);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Puts each selected thing's centre at a place and turns it to face a way (user story F-11), one
/// step of undo: what the inspector's fields do. What is left out stays, so <c>x</c> alone lines
/// the selection up; the height follows the terrain.
/// </summary>
/// <param name="X">In ogrids.</param>
/// <param name="Heading">In degrees, clockwise from +x in the top view.</param>
[UndoStep]
[EditorCommand("edit.place", "Place the selection", Story = "F-11", Needs = Needs.Selection)]
public sealed record PlaceSelection(float? X = null, float? Z = null, float? Heading = null) : IRequest;

public sealed class PlaceSelectionHandler(SelectionViewModel selection, CurrentEdit edit) : IRequestHandler<PlaceSelection>
{
    public Task Handle(PlaceSelection request, CancellationToken cancellationToken)
    {
        ImmutableArray<ObjectRef> things = selection.Items;
        edit.Transaction.Apply(document =>
        {
            if (request.X is not null || request.Z is not null)
            {
                document = document.PlaceObjects(things, request.X, request.Z);
            }
            return request.Heading is float heading ? document.SetHeading(things, heading * MathF.PI / 180) : document;
        });
        return Task.CompletedTask;
    }
}
