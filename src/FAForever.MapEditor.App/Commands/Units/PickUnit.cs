using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Workspaces;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Opens the picker for a unit, filtered by faction, kind and tech level, and shows the chosen
/// unit in the inspector (user story U-1); placing it comes with U-2. Run without a person, the
/// unit's id is the argument.
/// </summary>
/// <param name="Key">The unit to choose without asking, by its id (<c>uec1101</c>).</param>
/// <returns>The chosen unit's id; null when the picker was cancelled.</returns>
[EditorCommand("units.pick", "Choose a unit...", Story = "U-1")]
public sealed record PickUnit(string? Key = null) : IRequest<string?>;

public sealed class PickUnitHandler(MainViewModel model, AssetsViewModel assets, IEditorDialogs dialogs) : IRequestHandler<PickUnit, string?>
{
    public Task<string?> Handle(PickUnit request, CancellationToken cancellationToken) =>
        PickAssetHandler.PickAsync(model, assets, dialogs, AssetKind.Unit, request.Key, "Choose a unit");
}
