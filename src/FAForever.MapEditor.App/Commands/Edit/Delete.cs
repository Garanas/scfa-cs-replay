using System.Collections.Immutable;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Removes the selected things from the map (user story F-11), one step of undo, with every link
/// to a removed marker (path nodes, chains).
/// </summary>
[UndoStep]
[EditorCommand("edit.delete", "Delete", Gesture = "Delete", Story = "F-11", Needs = Needs.Selection)]
public sealed record Delete : IRequest;

public sealed class DeleteHandler(SelectionViewModel selection, CurrentEdit edit) : IRequestHandler<Delete>
{
    public Task Handle(Delete request, CancellationToken cancellationToken)
    {
        ImmutableArray<ObjectRef> things = selection.Items;
        edit.Transaction.Apply(document => document.RemoveObjects(things));
        selection.Clear();
        return Task.CompletedTask;
    }
}
