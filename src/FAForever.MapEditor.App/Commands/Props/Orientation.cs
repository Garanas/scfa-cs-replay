using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// The props among the selected things, by id.
/// </summary>
internal static class SelectedProps
{
    /// <exception cref="InvalidOperationException">No prop is selected.</exception>
    public static int[] Of(SelectionViewModel selection)
    {
        int[] ids = [.. selection.Items.Where(thing => thing.Kind == ObjectKind.Prop).Select(thing => thing.Id)];
        return ids.Length > 0 ? ids : throw new InvalidOperationException("Select props first: the Props mode's Select tool, or a box around them");
    }
}

/// <summary>
/// Tilts each selected prop so that its up follows the terrain's normal under it (user story P-1),
/// keeping its heading; one step of undo.
/// </summary>
[UndoStep]
[EditorCommand("props.alignToTerrain", "Align the props to the terrain", Story = "P-1", Needs = Needs.Selection)]
public sealed record AlignPropsToTerrain : IRequest;

public sealed class AlignPropsToTerrainHandler(SelectionViewModel selection, CurrentEdit edit) : IRequestHandler<AlignPropsToTerrain>
{
    public Task Handle(AlignPropsToTerrain request, CancellationToken cancellationToken)
    {
        int[] ids = SelectedProps.Of(selection);
        edit.Transaction.Apply(document => document.AlignPropsToTerrain(ids));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Stands each selected prop straight up (user story P-1), keeping its heading; one step of undo.
/// </summary>
[UndoStep]
[EditorCommand("props.upright", "Stand the props upright", Story = "P-1", Needs = Needs.Selection)]
public sealed record StandPropsUpright : IRequest;

public sealed class StandPropsUprightHandler(SelectionViewModel selection, CurrentEdit edit) : IRequestHandler<StandPropsUpright>
{
    public Task Handle(StandPropsUpright request, CancellationToken cancellationToken)
    {
        int[] ids = SelectedProps.Of(selection);
        edit.Transaction.Apply(document => document.StandPropsUpright(ids));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Puts each selected prop on the terrain, sunk into it by the Props panel's sink or the one given
/// (user story P-1); one step of undo.
/// </summary>
/// <param name="Sink">In game units into the terrain; left out, the Placing section's.</param>
[UndoStep]
[EditorCommand("props.dropToGround", "Drop the props to the ground", Story = "P-1", Needs = Needs.Selection)]
public sealed record DropPropsToGround(float? Sink = null) : IRequest;

public sealed class DropPropsToGroundHandler(MainViewModel model, SelectionViewModel selection, CurrentEdit edit) : IRequestHandler<DropPropsToGround>
{
    public Task Handle(DropPropsToGround request, CancellationToken cancellationToken)
    {
        int[] ids = SelectedProps.Of(selection);
        float sink = request.Sink ?? (float)model.Props.Sink;
        edit.Transaction.Apply(document => document.DropPropsToGround(ids, sink));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Gives the selected props a pitch, a roll or both, keeping their heading (user story P-1); one
/// step of undo: what the inspector's fields do.
/// </summary>
/// <param name="Pitch">In degrees, leaning each towards its own z; left out, each keeps its own.</param>
/// <param name="Roll">In degrees, leaning each towards its own -x; left out, each keeps its own.</param>
[UndoStep]
[EditorCommand("props.tilt", "Tilt the props", Story = "P-1", Needs = Needs.Selection)]
public sealed record TiltProps(float? Pitch = null, float? Roll = null) : IRequest;

public sealed class TiltPropsHandler(SelectionViewModel selection, CurrentEdit edit) : IRequestHandler<TiltProps>
{
    public Task Handle(TiltProps request, CancellationToken cancellationToken)
    {
        int[] ids = SelectedProps.Of(selection);
        if (request.Pitch is float pitch && MathF.Abs(pitch) >= 90 || request.Roll is float roll && MathF.Abs(roll) >= 90)
        {
            throw new ArgumentOutOfRangeException(nameof(request.Pitch), "A prop leans less than 90 degrees each way");
        }
        edit.Transaction.Apply(document => document.SetPropTilt(ids, request.Pitch * MathF.PI / 180, request.Roll * MathF.PI / 180));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Scales the selected props, the same along every axis (user story P-1); one step of undo.
/// </summary>
/// <param name="Scale">Above 0; 1 is the model's own size.</param>
[UndoStep]
[EditorCommand("props.scale", "Scale the props", Story = "P-1", Needs = Needs.Selection)]
public sealed record ScaleProps(float Scale) : IRequest;

public sealed class ScalePropsHandler(SelectionViewModel selection, CurrentEdit edit) : IRequestHandler<ScaleProps>
{
    public Task Handle(ScaleProps request, CancellationToken cancellationToken)
    {
        int[] ids = SelectedProps.Of(selection);
        edit.Transaction.Apply(document => document.SetPropScale(ids, request.Scale));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Gives the selected props a heading and a scale at random (user story P-1), one step of undo:
/// so a row of copies stops looking like copies.
/// </summary>
/// <param name="Turn">Whether each gets a heading at random; left out, yes.</param>
/// <param name="ScaleMin">The smallest scale; left out, the Placing section's.</param>
/// <param name="ScaleMax">The largest scale; left out, the Placing section's.</param>
[UndoStep]
[EditorCommand("props.randomize", "Turn and scale the props at random", Story = "P-1", Needs = Needs.Selection)]
public sealed record RandomizeProps(bool Turn = true, float? ScaleMin = null, float? ScaleMax = null) : IRequest;

public sealed class RandomizePropsHandler(MainViewModel model, SelectionViewModel selection, CurrentEdit edit) : IRequestHandler<RandomizeProps>
{
    public Task Handle(RandomizeProps request, CancellationToken cancellationToken)
    {
        int[] ids = SelectedProps.Of(selection);
        float low = request.ScaleMin ?? (float)Math.Min(model.Props.ScaleMin, model.Props.ScaleMax);
        float high = request.ScaleMax ?? (float)Math.Max(model.Props.ScaleMin, model.Props.ScaleMax);
        if (!(low > 0) || high < low)
        {
            throw new ArgumentOutOfRangeException(nameof(request.ScaleMin), "The scales are above 0, the smallest first");
        }
        Random random = model.Props.Random;
        edit.Transaction.Apply(document => document.ChangeProps(ids, prop =>
        {
            Prop changed = request.Turn ? PropOrientation.Oriented(prop, (float)(random.NextDouble() * 2 * Math.PI - Math.PI), PropOrientation.Up(prop)) : prop;
            return changed with { Scale = new System.Numerics.Vector3(low + (float)random.NextDouble() * (high - low)) };
        }));
        return Task.CompletedTask;
    }
}
