using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Workspaces;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Chooses the decal the Decals mode's place tool places, with the picker (user stories D-1,
/// F-9), and makes the place tool active. Run without a person, the decal's key is the argument.
/// </summary>
/// <param name="Key">The decal to choose without asking (<c>env/evergreen/decals/e_mud001_albedo.dds</c>), or a
/// decal set's name (<c>env/rocky/decals/rock01</c>) to place the whole set.</param>
/// <returns>The chosen decal's key; null when the picker was cancelled.</returns>
[EditorCommand("decals.choose", "Choose the decal to place...", Story = "D-1")]
public sealed record ChooseDecal(string? Key = null) : IRequest<string?>;

public sealed class ChooseDecalHandler(MainViewModel model, AssetsViewModel assets, IEditorDialogs dialogs) : IRequestHandler<ChooseDecal, string?>
{
    public async Task<string?> Handle(ChooseDecal request, CancellationToken cancellationToken)
    {
        // a decal set's name chooses the whole set (D-1); a texture's key, or the picker, one texture
        string? key = await PickAssetHandler.PickAsync(model, assets, dialogs, AssetKind.Decal, request.Key, "Choose the decal to place");
        if (key is null)
        {
            return null;
        }
        model.DecalPlaceTool.Decal = key;
        if (model.SelectedMode?.Definition.Id != "decals")
        {
            model.SelectMode("decals");
        }
        model.SelectTool("decals.place");
        return key;
    }
}
