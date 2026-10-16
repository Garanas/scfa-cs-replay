using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Removes a mask for tools (user story K-1), one step of undo; its file goes from the project on
/// the next save.
/// </summary>
/// <param name="Name">The mask; left out, the one chosen in the panel.</param>
[UndoStep]
[EditorCommand("paint.deleteMask", "Delete the mask", Story = "K-1")]
public sealed record DeleteMask(string? Name = null) : IRequest;

public sealed class DeleteMaskHandler(MainViewModel model, CurrentEdit edit) : IRequestHandler<DeleteMask>
{
    public Task Handle(DeleteMask request, CancellationToken cancellationToken)
    {
        string name = request.Name ?? model.Masks.Chosen ?? throw new ArgumentException("Choose the mask to delete, or give its name");
        edit.Transaction.Apply(document => MaskLayers.Remove(document, name));
        model.Status = $"Deleted the mask {name}";
        return Task.CompletedTask;
    }
}
