using FAForever.MapEditor.App.ViewModels;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Converts the game's assets into the workspace; the editor waits until it is done (user story F-5).
/// </summary>
/// <returns>Whether the conversion finished.</returns>
[EditorCommand("workspace.convert", "Update the assets", Story = "F-5")]
public sealed record ConvertAssets : IRequest<bool>;

public sealed class ConvertAssetsHandler(MainViewModel model) : IRequestHandler<ConvertAssets, bool>
{
    public Task<bool> Handle(ConvertAssets request, CancellationToken cancellationToken) => model.ConvertAssetsAsync();
}
