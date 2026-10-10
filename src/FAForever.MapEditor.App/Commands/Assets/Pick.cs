using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Workspaces;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Opens the picker for an asset of the active mode's kind and shows the chosen one in the
/// inspector, as the shelf does (user story F-9). Run without a person, the key is the argument.
/// </summary>
/// <param name="Kind"><c>prop</c>, <c>unit</c>, <c>decal</c>, <c>texture</c> or <c>brush</c>; left out,
/// the active mode's (Terrain brushes, Paint textures, ...), else the shelf's.</param>
/// <param name="Key">The asset to choose without asking: a game path or a unit's id.</param>
/// <returns>The chosen asset's key; null when the picker was cancelled.</returns>
[EditorCommand("assets.pick", "Find an asset...", Gesture = "Ctrl+P", Story = "F-9")]
public sealed record PickAsset(string? Kind = null, string? Key = null) : IRequest<string?>;

public sealed class PickAssetHandler(MainViewModel model, AssetsViewModel assets, IEditorDialogs dialogs) : IRequestHandler<PickAsset, string?>
{
    public Task<string?> Handle(PickAsset request, CancellationToken cancellationToken)
    {
        AssetKind kind = request.Kind is string named
            ? Enum.TryParse(named, ignoreCase: true, out AssetKind parsed) && Enum.IsDefined(parsed)
                ? parsed
                : throw new ArgumentException($"There is no kind of asset '{named}': prop, unit, decal, texture or brush")
            : AssetsViewModel.KindOfMode(model.SelectedMode?.Definition.Id) ?? assets.Kind;
        // a key without a kind chooses whatever it names
        return PickAsync(model, assets, dialogs, kind, request.Key, title: null, anyKind: request.Kind is null);
    }

    /// <summary>
    /// The key given, else the asset the person picks (starting on the one the inspector shows);
    /// the chosen asset then shows in the inspector.
    /// </summary>
    /// <param name="anyKind">Whether a key given may name an asset of another kind.</param>
    /// <exception cref="ArgumentException">The workspace has no asset of that key, or of that kind.</exception>
    public static async Task<string?> PickAsync(MainViewModel model, AssetsViewModel assets, IEditorDialogs dialogs, AssetKind kind, string? key, string? title, bool anyKind = false)
    {
        string? current = assets.Shown is { } shown && shown.Kind == kind ? shown.Key : null;
        string? chosen = key ?? await dialogs.PickAssetAsync(kind, title, current);
        if (chosen is null)
        {
            model.Status = "Chose nothing";
            return null;
        }
        AssetEntry entry = assets.Catalog.Find(chosen) ?? throw new ArgumentException($"The workspace has no asset '{chosen}'");
        if (entry.Kind != kind && !anyKind)
        {
            throw new ArgumentException($"'{chosen}' is a {AssetsViewModel.Noun(entry.Kind, 1).ToLowerInvariant()}, not a {AssetsViewModel.Noun(kind, 1).ToLowerInvariant()}");
        }
        assets.Select(entry.Key);
        model.Status = $"Chose {entry.Name} ({AssetsViewModel.Noun(entry.Kind, 1).ToLowerInvariant()})";
        return entry.Key;
    }
}
