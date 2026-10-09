using FAForever.MapEditor.App.ViewModels;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Undoes the last step of the history (user story F-3).
/// </summary>
/// <returns>Whether a step was undone.</returns>
[EditorCommand("edit.undo", "Undo", Gesture = "Ctrl+Z", Story = "F-3", Needs = Needs.Undo)]
public sealed record Undo : IRequest<bool>;

public sealed class UndoHandler(MainViewModel model) : IRequestHandler<Undo, bool>
{
    public Task<bool> Handle(Undo request, CancellationToken cancellationToken) => Task.FromResult(model.Undo());
}
