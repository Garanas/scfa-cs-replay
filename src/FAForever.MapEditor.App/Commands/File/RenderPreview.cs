using FAForever.MapEditor.App.Rendering;
using FAForever.MapEditor.App.ViewModels;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Renders the map's preview (user stories M-8, S-4): the terrain from above, 256 by 256 pixels as
/// the game's, generated on a thread of the pool and put in the map: one step of undo. It is out of
/// date when the heightmap or the water level changes.
/// </summary>
[EditorCommand("file.renderPreview", "Render the preview", Story = "M-8", Needs = Needs.Map)]
public sealed record RenderPreview : IRequest;

public sealed class RenderPreviewHandler(MainViewModel model) : IRequestHandler<RenderPreview>
{
    public async Task Handle(RenderPreview request, CancellationToken cancellationToken)
    {
        await model.GenerateAsync(MapPreview.GeneratorId);
        model.Status = "Rendered the preview from the terrain";
    }
}
