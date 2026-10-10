using FAForever.MapEditor.App.ViewModels;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Opens the keyboard shortcuts page (user story F-1): every command's shortcut, the keys of the
/// tools and the mouse in each view, with a search.
/// </summary>
/// <param name="Query">Words to search for at once, e.g. <c>undo</c>.</param>
/// <returns>How many lines show.</returns>
[EditorCommand("help.shortcuts", "Keyboard shortcuts", Gesture = "Shift+F1", Story = "F-1")]
public sealed record ShowShortcuts(string? Query = null) : IRequest<int>;

public sealed class ShowShortcutsHandler(MainViewModel model) : IRequestHandler<ShowShortcuts, int>
{
    public Task<int> Handle(ShowShortcuts request, CancellationToken cancellationToken)
    {
        model.Shortcuts.Open(request.Query);
        return Task.FromResult(model.Shortcuts.Count);
    }
}
