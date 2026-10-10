using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Gives a mask for tools another name (user story K-1), one step of undo; its file in the
/// project follows on the next save.
/// </summary>
/// <param name="Name">The mask; left out, the one chosen in the panel.</param>
/// <param name="NewName">Left out, the name in the panel's field.</param>
[UndoStep]
[EditorCommand("paint.renameMask", "Rename the mask", Story = "K-1")]
public sealed record RenameMask(string? Name = null, string? NewName = null) : IRequest;

public sealed class RenameMaskHandler(MainViewModel model, CurrentEdit edit) : IRequestHandler<RenameMask>
{
    public Task Handle(RenameMask request, CancellationToken cancellationToken)
    {
        MasksViewModel masks = model.Masks;
        string name = request.Name ?? masks.Chosen ?? throw new ArgumentException("Choose the mask to rename, or give its name");
        string newName = (request.NewName ?? masks.NewName).Trim();
        edit.Transaction.Apply(document => MaskLayers.Rename(document, name, newName));
        masks.NewName = "";
        if (masks.Chosen == name)
        {
            masks.Chosen = newName;
        }
        model.Status = $"Renamed the mask {name} to {newName}";
        return Task.CompletedTask;
    }
}
