using System.Collections.Immutable;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Scales each selected decal about its own centre (user story D-1), one step of undo: both
/// sides by <c>factor</c>, or the width by <c>x</c> and the length by <c>z</c>. What plus and minus
/// do with the Decals mode's select tool. While the symmetry mirrors edits, the decals' mirror
/// partners scale alike.
/// </summary>
/// <param name="Factor">Both sides; 1.1 makes them a tenth larger.</param>
/// <param name="X">The width alone (along each decal's own x axis), times <c>factor</c> when both are given.</param>
/// <param name="Z">The length alone.</param>
[UndoStep]
[EditorCommand("decals.scale", "Scale the decals", Story = "D-1", Needs = Needs.Selection)]
public sealed record ScaleDecals(float? Factor = null, float? X = null, float? Z = null) : IRequest;

public sealed class ScaleDecalsHandler(MainViewModel model, SelectionViewModel selection, CurrentEdit edit) : IRequestHandler<ScaleDecals>
{
    public Task Handle(ScaleDecals request, CancellationToken cancellationToken)
    {
        float factor = request.Factor ?? 1;
        float x = factor * (request.X ?? 1);
        float z = factor * (request.Z ?? 1);
        if (!(x > 0) || !(z > 0) || !float.IsFinite(x) || !float.IsFinite(z))
        {
            throw new ArgumentException("A decal is scaled by a number larger than 0");
        }
        ImmutableArray<ObjectRef> decals = SelectedDecals.Of(selection);
        edit.Transaction.Apply(document => document.ScaleDecals(SelectedDecals.WithPartners(model, document, decals), x, z));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Gives each selected decal a width or a length in ogrids (user story D-1), about its own centre,
/// one step of undo: what the inspector's fields do. What is left out stays. While the symmetry
/// mirrors edits, the decals' mirror partners get the same width and length.
/// </summary>
[UndoStep]
[EditorCommand("decals.size", "Size the decals", Story = "D-1", Needs = Needs.Selection)]
public sealed record SizeDecals(float? Width = null, float? Length = null) : IRequest;

public sealed class SizeDecalsHandler(MainViewModel model, SelectionViewModel selection, CurrentEdit edit) : IRequestHandler<SizeDecals>
{
    public Task Handle(SizeDecals request, CancellationToken cancellationToken)
    {
        if (request.Width is <= 0 || request.Length is <= 0)
        {
            throw new ArgumentException("A decal's width and length are larger than 0");
        }
        ImmutableArray<ObjectRef> decals = SelectedDecals.Of(selection);
        edit.Transaction.Apply(document => document.SizeDecals(SelectedDecals.WithPartners(model, document, decals), request.Width, request.Length));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Sets the distances at which the selected decals show in the game (user story D-1), for one or
/// many at once, one step of undo: beyond <c>cutOff</c> and closer than <c>nearCutOff</c> the game
/// does not draw them. What is left out stays.
/// </summary>
[UndoStep]
[EditorCommand("decals.lod", "Set the distances of the decals", Story = "D-1", Needs = Needs.Selection)]
public sealed record SetDecalLods(float? CutOff = null, float? NearCutOff = null) : IRequest;

public sealed class SetDecalLodsHandler(SelectionViewModel selection, CurrentEdit edit) : IRequestHandler<SetDecalLods>
{
    public Task Handle(SetDecalLods request, CancellationToken cancellationToken)
    {
        if (request.CutOff is < 0 || request.NearCutOff is < 0)
        {
            throw new ArgumentException("A decal's distances are 0 or more");
        }
        ImmutableArray<ObjectRef> decals = SelectedDecals.Of(selection);
        edit.Transaction.Apply(document => document.SetDecalLods(decals, request.CutOff, request.NearCutOff));
        return Task.CompletedTask;
    }
}

/// <summary>
/// The decals of the selection, for the commands of the Decals mode.
/// </summary>
internal static class SelectedDecals
{
    /// <exception cref="InvalidOperationException">No decal is selected.</exception>
    public static ImmutableArray<ObjectRef> Of(SelectionViewModel selection)
    {
        ImmutableArray<ObjectRef> decals = [.. selection.Items.Where(thing => thing.Kind == ObjectKind.Decal)];
        return decals.IsEmpty ? throw new InvalidOperationException("No decal is selected: select decals first") : decals;
    }

    /// <summary>
    /// The decals and, while the symmetry mirrors edits, their mirror partners (user story F-12):
    /// what scales about its own centre takes its partners along.
    /// </summary>
    public static ImmutableArray<ObjectRef> WithPartners(MainViewModel model, MapDocument document, ImmutableArray<ObjectRef> decals) =>
        [.. decals, .. model.PartnersOf(document, decals).Select(partner => partner.Partner)];
}
