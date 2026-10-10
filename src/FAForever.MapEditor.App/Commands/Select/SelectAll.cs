using FAForever.MapEditor.App.ViewModels;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Selects every thing that can be picked now (user story F-11): of the active select tool's kinds,
/// else the mode's (the Markers mode its markers and areas, the Units mode its units, ...), that
/// the views show.
/// </summary>
/// <returns>How many things are selected.</returns>
[EditorCommand("select.all", "Select all", Gesture = "Ctrl+A", Story = "F-11", Needs = Needs.Map)]
public sealed record SelectAll : IRequest<int>;

public sealed class SelectAllHandler(MainViewModel model) : IRequestHandler<SelectAll, int>
{
    public Task<int> Handle(SelectAll request, CancellationToken cancellationToken)
    {
        model.SelectAll();
        return Task.FromResult(model.Selection.Count);
    }
}

/// <summary>
/// Selects nothing (user story F-11), as Escape does in a view with the select tool.
/// </summary>
[EditorCommand("select.none", "Select none", Story = "F-11")]
public sealed record SelectNone : IRequest;

public sealed class SelectNoneHandler(SelectionViewModel selection) : IRequestHandler<SelectNone>
{
    public Task Handle(SelectNone request, CancellationToken cancellationToken)
    {
        selection.Clear();
        return Task.CompletedTask;
    }
}
