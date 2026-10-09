using FAForever.MapEditor.App.ViewModels;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Undoes or redoes up to a step of the history (user story F-4), as choosing a line of the
/// History panel does.
/// </summary>
/// <param name="Step">The step; -1 for the map as it was opened.</param>
/// <returns>Whether the map moved to that step.</returns>
[EditorCommand("edit.goTo", "Go to a step of the history", Story = "F-4", Needs = Needs.Map | Needs.NoStroke)]
public sealed record GoTo(int Step) : IRequest<bool>;

public sealed class GoToHandler(MainViewModel model) : IRequestHandler<GoTo, bool>
{
    public Task<bool> Handle(GoTo request, CancellationToken cancellationToken) => Task.FromResult(model.GoTo(request.Step));
}
