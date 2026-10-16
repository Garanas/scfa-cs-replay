using FAForever.MapEditor.App.ViewModels;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Shows or hides what the views show over the terrain. Its commands, <c>view.overlay.&lt;id&gt;</c>,
/// are made from <see cref="EditorOverlays"/>.
/// </summary>
/// <param name="On">Whether to show it; left out, it turns over.</param>
public sealed record ToggleOverlay(string Id, bool? On = null) : IRequest;

public sealed class ToggleOverlayHandler(MainViewModel model) : IRequestHandler<ToggleOverlay>
{
    public Task Handle(ToggleOverlay request, CancellationToken cancellationToken)
    {
        OverlayItem overlay = model.Overlays.FirstOrDefault(o => o.Id == request.Id)
            ?? throw new ArgumentException($"There is no overlay '{request.Id}'");
        overlay.IsOn = request.On ?? !overlay.IsOn;
        return Task.CompletedTask;
    }
}
