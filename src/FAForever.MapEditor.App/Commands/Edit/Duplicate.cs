using System.Collections.Immutable;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Copies the selected things next to themselves (user story F-11), one step of undo, and selects
/// the copies; what was copied before stays for <c>edit.paste</c>. While the symmetry mirrors
/// edits, the copies get their images in the other parts of the map, as placed things do: a
/// selected thing's selected partner is not copied itself, its copy is the image of the thing's.
/// </summary>
/// <param name="Dx">How far from the originals, in ogrids; left out, 2 ogrids to the right and down.</param>
/// <returns>The copies, as references.</returns>
[UndoStep]
[EditorCommand("edit.duplicate", "Duplicate", Gesture = "Ctrl+D", Story = "F-11", Needs = Needs.Selection)]
public sealed record Duplicate(float? Dx = null, float? Dz = null) : IRequest<string[]>;

public sealed class DuplicateHandler(MainViewModel model, SelectionViewModel selection, CurrentEdit edit) : IRequestHandler<Duplicate, string[]>
{
    /// <summary>
    /// How far a copy lies from its original when the request says nothing, in ogrids: beside it,
    /// so that it shows.
    /// </summary>
    public const float Offset = 2;

    public Task<string[]> Handle(Duplicate request, CancellationToken cancellationToken)
    {
        ImmutableArray<ObjectRef> things = selection.Items;
        ImmutableArray<ObjectRef> added = [];
        // with the symmetry, the things without their selected partners are copied, and the copies
        // get their images in the other parts, as placed things do (F-12): a partner's copy is the
        // image of its thing's copy
        edit.Transaction.Apply(document =>
        {
            ImmutableArray<ObjectRef> originals = model.Symmetry.IsMirroring ? document.WithoutPartners(things, model.Symmetry.Transforms(document)) : things;
            document = document.Paste(document.CopyObjects(originals), request.Dx ?? Offset, request.Dz ?? Offset, out ImmutableArray<ObjectRef> copies);
            return model.MirrorPlaced(document, copies, out added);
        });
        selection.Set(added);
        return Task.FromResult(added.Select(thing => thing.ToString()).ToArray());
    }
}
