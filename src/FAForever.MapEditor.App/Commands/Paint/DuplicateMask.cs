using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Copies a mask for tools with its stack of layers under a new name (user story K-1), one step of
/// undo, and chooses the copy: a start for a mask like another (the forest, and the forest's edge).
/// </summary>
/// <param name="Name">The mask; left out, the one chosen in the panel.</param>
/// <param name="NewName">Left out, the name in the panel's field, else a free one (<c>forest copy</c>).</param>
/// <returns>The copy's name.</returns>
[UndoStep]
[EditorCommand("paint.duplicateMask", "Duplicate the mask", Story = "K-1")]
public sealed record DuplicateMask(string? Name = null, string? NewName = null) : IRequest<string>;

public sealed class DuplicateMaskHandler(MainViewModel model, CurrentEdit edit) : IRequestHandler<DuplicateMask, string>
{
    public Task<string> Handle(DuplicateMask request, CancellationToken cancellationToken)
    {
        MasksViewModel masks = model.Masks;
        string name = request.Name ?? masks.Chosen ?? throw new ArgumentException("Choose the mask to duplicate, or give its name");
        string newName = request.NewName?.Trim() is { Length: > 0 } given ? given
            : masks.NewName.Trim() is { Length: > 0 } typed ? typed
            : MaskLayers.FreeName(edit.Transaction.Document, $"{name} copy");
        edit.Transaction.Apply(document => MaskLayers.Duplicate(document, name, newName));
        masks.NewName = "";
        masks.Chosen = newName;
        model.Status = $"Duplicated the mask {name} as {newName}";
        return Task.FromResult(newName);
    }
}
