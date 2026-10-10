using System.Collections.Immutable;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Copies the selected things next to themselves (user story F-11), one step of undo, and selects
/// the copies; what was copied before stays for <c>edit.paste</c>.
/// </summary>
/// <param name="Dx">How far from the originals, in ogrids; left out, 2 ogrids to the right and down.</param>
/// <returns>The copies, as references.</returns>
[UndoStep]
[EditorCommand("edit.duplicate", "Duplicate", Gesture = "Ctrl+D", Story = "F-11", Needs = Needs.Selection)]
public sealed record Duplicate(float? Dx = null, float? Dz = null) : IRequest<string[]>;

public sealed class DuplicateHandler(SelectionViewModel selection, CurrentEdit edit) : IRequestHandler<Duplicate, string[]>
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
        edit.Transaction.Apply(document => document.Paste(document.CopyObjects(things), request.Dx ?? Offset, request.Dz ?? Offset, out added));
        selection.Set(added);
        return Task.FromResult(added.Select(thing => thing.ToString()).ToArray());
    }
}
