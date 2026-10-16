using FAForever.MapEditor.App.ViewModels;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Sends the 3D camera to see the whole selection (user story F-11), gliding while the 3D view
/// shows: what F does in the 3D view while something is selected, as in the FAF map editor.
/// </summary>
[EditorCommand("camera.focusSelection", "Focus the 3D camera on the selection", Story = "F-11", Needs = Needs.Selection)]
public sealed record FocusSelection : IRequest;

public sealed class FocusSelectionHandler(MainViewModel model) : IRequestHandler<FocusSelection>
{
    public Task Handle(FocusSelection request, CancellationToken cancellationToken)
    {
        model.FocusOnSelection();
        return Task.CompletedTask;
    }
}
