using FAForever.MapEditor.Project.Editing;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Links two path nodes of one layer both ways, or takes their link away (user story R-3), one step
/// of undo: what dragging from one node to another with the path tool does (with control, unlinks).
/// </summary>
/// <param name="A">The id of a path node (<c>marker:12</c> is 12).</param>
/// <param name="B">The id of the other.</param>
/// <param name="Unlink">Takes the link away instead.</param>
[UndoStep]
[EditorCommand("markers.link", "Link path nodes", Story = "R-3", Needs = Needs.Map)]
public sealed record LinkPathNodes(int A, int B, bool Unlink = false) : IRequest;

public sealed class LinkPathNodesHandler(CurrentEdit edit) : IRequestHandler<LinkPathNodes>
{
    public Task Handle(LinkPathNodes request, CancellationToken cancellationToken)
    {
        edit.Transaction.Apply(document => request.Unlink ? document.UnlinkPathNodes(request.A, request.B) : document.LinkPathNodes(request.A, request.B));
        return Task.CompletedTask;
    }
}
