using System.Collections.Immutable;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Removes the selected things from the map (user story F-11), one step of undo, with every link
/// to a removed marker (path nodes, chains). While the symmetry mirrors edits, the things' mirror
/// partners go with them. In the Sketch mode it removes the sketch's selected items (<c>sketch.deleteSelected</c>)
/// and nothing else: the map's things selected before stay.
/// </summary>
[UndoStep]
[EditorCommand("edit.delete", "Delete", Gesture = "Delete", Story = "F-11", Needs = Needs.Selection)]
public sealed record Delete : IRequest;

public sealed class DeleteHandler(MainViewModel model, SelectionViewModel selection, CurrentEdit edit) : IRequestHandler<Delete>
{
    public Task Handle(Delete request, CancellationToken cancellationToken)
    {
        if (model.InSketchMode)
        {
            return new DeleteSketchSelectionHandler(model, edit).Handle(new DeleteSketchSelection(), cancellationToken);
        }
        ImmutableArray<ObjectRef> things = selection.Items;
        string deleted = selection.Summary;
        edit.Transaction.Apply(document => document.RemoveObjects(things.Concat(model.PartnersOf(document, things).Select(partner => partner.Partner)).Distinct()));
        selection.Clear();
        model.Status = $"Deleted {deleted}";
        return Task.CompletedTask;
    }
}
