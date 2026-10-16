using FAForever.MapEditor.App.ViewModels;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Goes to a problem of the Problems tab (user story F-14), as a double click or Enter on its line
/// does: selects its thing, and the top view and the 3D camera look at its place. A click on its line
/// only selects (<paramref name="Frame"/> false).
/// </summary>
/// <param name="Index">The problem's line, from 0.</param>
/// <param name="Frame">Whether the views look at its place; false selects the things and leaves the views.</param>
/// <returns>Whether it led anywhere: a problem of the whole map has no place.</returns>
[EditorCommand("problems.show", "Go to a problem", Story = "F-14", Needs = Needs.Map)]
public sealed record ShowProblem(int Index, bool Frame = true) : IRequest<bool>;

public sealed class ShowProblemHandler(MainViewModel model) : IRequestHandler<ShowProblem, bool>
{
    public Task<bool> Handle(ShowProblem request, CancellationToken cancellationToken) => Task.FromResult(model.ShowProblem(request.Index, request.Frame));
}
