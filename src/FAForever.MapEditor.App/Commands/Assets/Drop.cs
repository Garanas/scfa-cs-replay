using FAForever.MapEditor.App.ViewModels;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Drops an asset on the map at a place, as dragging a tile of the shelf onto a view does: the
/// active tool takes it, or whoever handles <see cref="MainViewModel.AssetDropped"/> (user story
/// F-9). For automation; people drag.
/// </summary>
/// <param name="Key">The asset: a game path or a unit's id.</param>
/// <param name="X">The place, in ogrids from the map's west edge.</param>
/// <param name="Z">The place, in ogrids from the map's north edge.</param>
/// <returns>Whether something took the drop.</returns>
[EditorCommand("assets.drop", "Drop an asset on the map", Story = "F-9", Needs = Needs.Map)]
public sealed record DropAsset(string Key, float X, float Z) : IRequest<bool>;

public sealed class DropAssetHandler(MainViewModel model) : IRequestHandler<DropAsset, bool>
{
    public Task<bool> Handle(DropAsset request, CancellationToken cancellationToken) =>
        Task.FromResult(model.DropAsset(request.Key, request.X, request.Z));
}
