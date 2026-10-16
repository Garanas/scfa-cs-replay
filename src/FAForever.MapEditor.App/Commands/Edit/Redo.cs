using FAForever.MapEditor.App.ViewModels;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Does the last undone step again (user story F-3).
/// </summary>
/// <returns>Whether a step was redone.</returns>
[EditorCommand("edit.redo", "Redo", Gesture = "Ctrl+Y", Story = "F-3", Needs = Needs.Redo)]
public sealed record Redo : IRequest<bool>;

public sealed class RedoHandler(MainViewModel model) : IRequestHandler<Redo, bool>
{
    public Task<bool> Handle(Redo request, CancellationToken cancellationToken) => Task.FromResult(model.Redo());
}
