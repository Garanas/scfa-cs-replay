using System.Collections.Immutable;
using System.Numerics;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Pastes what was copied (user story F-11), one step of undo, and selects the copies: centred on
/// a place, else where the mouse is on the map, else where the originals were; then moved by an
/// offset. While the symmetry mirrors edits, the copies get their images in the other parts of the
/// map, as placed things do, and those are selected too.
/// </summary>
/// <param name="X">Where the middle of the copies goes, in ogrids.</param>
/// <param name="Dx">How far to move them after that, in ogrids.</param>
/// <returns>The copies, as references (<c>marker:12</c>, ...).</returns>
[UndoStep]
[EditorCommand("edit.paste", "Paste", Gesture = "Ctrl+V", Story = "F-11")]
public sealed record Paste(float? X = null, float? Z = null, float? Dx = null, float? Dz = null) : IRequest<string[]>;

public sealed class PasteHandler(MainViewModel model, SelectionViewModel selection, CurrentEdit edit) : IRequestHandler<Paste, string[]>
{
    public Task<string[]> Handle(Paste request, CancellationToken cancellationToken)
    {
        ObjectClipboard clipboard = selection.Clipboard is { IsEmpty: false } copied
            ? copied
            : throw new InvalidOperationException("Nothing is copied: select things on the map and copy them first");
        Vector2 centre = clipboard.Centre;
        (float x, float z) = (request.X, request.Z, model.Pointer) switch
        {
            (float px, float pz, _) => (px, pz),
            (null, null, (float mx, float mz)) => (mx, mz),
            _ => (request.X ?? centre.X, request.Z ?? centre.Y),
        };
        ImmutableArray<ObjectRef> added = [];
        // with the symmetry, the copies get their images in the other parts, as placed things do (F-12)
        edit.Transaction.Apply(document => model.MirrorPlaced(document.Paste(clipboard, x - centre.X + (request.Dx ?? 0), z - centre.Y + (request.Dz ?? 0), out ImmutableArray<ObjectRef> copies), copies, out added));
        selection.Set(added);
        return Task.FromResult(added.Select(thing => thing.ToString()).ToArray());
    }
}
