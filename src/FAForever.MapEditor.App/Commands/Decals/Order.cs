using System.Collections.Immutable;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Brings the selected decals one place forward in the map's order (user story D-1), one step of
/// undo: the order decides what lies on top, the last drawn last.
/// </summary>
[UndoStep]
[EditorCommand("decals.forward", "Bring forward", Gesture = "Ctrl+OemCloseBrackets", Story = "D-1", Needs = Needs.Selection)]
public sealed record BringDecalsForward : IRequest;

/// <summary>
/// Sends the selected decals one place back in the map's order (user story D-1).
/// </summary>
[UndoStep]
[EditorCommand("decals.backward", "Send backward", Gesture = "Ctrl+OemOpenBrackets", Story = "D-1", Needs = Needs.Selection)]
public sealed record SendDecalsBackward : IRequest;

/// <summary>
/// Brings the selected decals on top of every other decal (user story D-1), keeping their own order.
/// </summary>
[UndoStep]
[EditorCommand("decals.toFront", "Bring to front", Gesture = "Ctrl+Shift+OemCloseBrackets", Story = "D-1", Needs = Needs.Selection)]
public sealed record BringDecalsToFront : IRequest;

/// <summary>
/// Sends the selected decals under every other decal (user story D-1), keeping their own order.
/// </summary>
[UndoStep]
[EditorCommand("decals.toBack", "Send to back", Gesture = "Ctrl+Shift+OemOpenBrackets", Story = "D-1", Needs = Needs.Selection)]
public sealed record SendDecalsToBack : IRequest;

public sealed class OrderDecalsHandler(SelectionViewModel selection, CurrentEdit edit) :
    IRequestHandler<BringDecalsForward>, IRequestHandler<SendDecalsBackward>, IRequestHandler<BringDecalsToFront>, IRequestHandler<SendDecalsToBack>
{
    public Task Handle(BringDecalsForward request, CancellationToken cancellationToken) => Order(DecalOrder.Forward);

    public Task Handle(SendDecalsBackward request, CancellationToken cancellationToken) => Order(DecalOrder.Backward);

    public Task Handle(BringDecalsToFront request, CancellationToken cancellationToken) => Order(DecalOrder.ToFront);

    public Task Handle(SendDecalsToBack request, CancellationToken cancellationToken) => Order(DecalOrder.ToBack);

    private Task Order(DecalOrder order)
    {
        ImmutableArray<ObjectRef> decals = SelectedDecals.Of(selection);
        edit.Transaction.Apply(document => document.OrderDecals(decals, order));
        return Task.CompletedTask;
    }
}
