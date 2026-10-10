using System.Text.Json.Nodes;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Workspaces;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Stars an asset or takes its star away (user story F-9): starred assets show under Starred on the
/// shelf and in the picker, in every map of the workspace (its <c>favourites.json</c>). Left out,
/// the asset the open picker has selected, else the one the inspector shows; <c>on</c> left out
/// turns its star around.
/// </summary>
/// <param name="Key">A game path or a unit's id.</param>
/// <returns>The asset's key and whether it is starred now.</returns>
[EditorCommand("assets.star", "Star the asset", Story = "F-9")]
public sealed record StarAsset(string? Key = null, bool? On = null) : IRequest<JsonObject>;

public sealed class StarAssetHandler(MainViewModel model, AssetsViewModel assets) : IRequestHandler<StarAsset, JsonObject>
{
    public Task<JsonObject> Handle(StarAsset request, CancellationToken cancellationToken)
    {
        string key = request.Key
            ?? (model.Picker.IsOpen ? model.Picker.Selected?.Entry.Key : null)
            ?? assets.Shown?.Key
            ?? throw new ArgumentException("Choose an asset on the shelf or in the picker, or give its key");
        AssetEntry entry = assets.Catalog.Find(key) ?? throw new ArgumentException($"The workspace has no asset '{key}'");
        bool starred = assets.SetStar(entry.Key, request.On ?? !assets.Favourites.IsStarred(entry.Key));
        model.Status = starred ? $"Starred {entry.Name}: it shows under Starred" : $"Took the star of {entry.Name} away";
        return Task.FromResult(new JsonObject { ["key"] = entry.Key, ["starred"] = starred });
    }
}
