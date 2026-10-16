using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// The paths a stratum texture of the workspace gives a layer: its albedo and its normal map, as
/// game paths (<c>/env/tundra/layers/tund_rock_albedo.dds</c>).
/// </summary>
internal static class LayerTexture
{
    /// <exception cref="ArgumentException">The workspace has no such texture.</exception>
    public static (string? Albedo, string? Normal) Of(AssetsViewModel assets, string key)
    {
        AssetEntry entry = assets.Catalog.Find(key) is { Kind: AssetKind.Texture } found
            ? found
            : throw new ArgumentException($"The workspace has no stratum texture '{key}' (Modes, Assets: the Paint mode's textures)");
        bool albedo = entry.Category != "normal map only";
        string? normal = entry.Keys.Length > 1 ? entry.Keys[1] : albedo ? null : entry.Keys[0];
        return (albedo ? "/" + entry.Keys[0] : null, normal is null ? null : "/" + normal);
    }
}

/// <summary>
/// Gives a layer a texture of the workspace (user story S-2), one step of undo: an albedo texture
/// with its normal map when it has one, or with <c>normal</c> only a normal map. With a person and
/// without a key, the picker of the strata textures asks for it.
/// </summary>
/// <param name="Layer">0 the lower layer, then the strata, the last the upper layer; left out, the
/// one the Paint panel's list has chosen.</param>
/// <param name="Key">The texture, as the shelf names it: <c>env/tundra/layers/tund_rock_albedo.dds</c>.</param>
/// <param name="Normal">Whether to choose only the normal map.</param>
[EditorCommand("paint.chooseTexture", "Choose the layer's texture...", Story = "S-2", Needs = Needs.Map | Needs.NoStroke)]
public sealed record ChooseLayerTexture(int? Layer = null, string? Key = null, bool Normal = false) : IRequest<string?>;

public sealed class ChooseLayerTextureHandler(MainViewModel model, AssetsViewModel assets, IEditorDialogs dialogs) : IRequestHandler<ChooseLayerTexture, string?>
{
    public async Task<string?> Handle(ChooseLayerTexture request, CancellationToken cancellationToken)
    {
        MapDocument document = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
        int layer = request.Layer ?? model.LayerTextures.SelectedIndex;
        if (layer < 0 || layer >= document.Terrain.Layers.Length)
        {
            throw new ArgumentException($"The map has layers 0 to {document.Terrain.Layers.Length - 1}");
        }
        string title = LayerTexturesViewModel.TitleOf(layer, document.Terrain.Layers.Length);
        TerrainLayer current = document.Terrain.Layers[layer];
        string? key = request.Key ?? await dialogs.PickAssetAsync(AssetKind.Texture,
            request.Normal ? $"Choose the normal map of the {title.ToLowerInvariant()}" : $"Choose the texture of the {title.ToLowerInvariant()}",
            assets.Catalog.Find(request.Normal ? current.NormalPath : current.AlbedoPath)?.Key);
        if (key is null)
        {
            model.Status = "Chose nothing";
            return null;
        }
        (string? albedo, string? normal) = LayerTexture.Of(assets, key);
        if (request.Normal && normal is null)
        {
            throw new ArgumentException($"The texture '{key}' has no normal map");
        }
        if (!request.Normal && albedo is null)
        {
            throw new ArgumentException($"'{key}' is only a normal map: choose it with normal");
        }
        model.EditMap($"{title}: {Path.GetFileNameWithoutExtension(request.Normal ? normal : albedo)}", d => d.UpdateLayer(layer, l => request.Normal
            ? l with { NormalPath = normal! }
            : l with { AlbedoPath = albedo!, NormalPath = normal ?? l.NormalPath }));
        model.LayerTextures.Choose(layer);
        model.Status = $"The {title.ToLowerInvariant()} has the texture {key}";
        return key;
    }
}

/// <summary>
/// Changes a layer's textures by their paths or their scales (user story S-2), one step of undo;
/// the steps that change the same field of the same layer one after the other are one step. What
/// the inspector's fields do.
/// </summary>
/// <param name="Albedo">The albedo texture's game path; empty for none (an unused stratum).</param>
/// <param name="AlbedoScale">How many ogrids the texture repeats over.</param>
[EditorCommand("paint.setLayer", "Change a layer", Story = "S-2", Needs = Needs.Map | Needs.NoStroke)]
public sealed record SetLayer(int Layer, string? Albedo = null, string? Normal = null, float? AlbedoScale = null, float? NormalScale = null) : IRequest;

public sealed class SetLayerHandler(MainViewModel model) : IRequestHandler<SetLayer>
{
    public Task Handle(SetLayer request, CancellationToken cancellationToken)
    {
        MapDocument document = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
        if (request.Layer < 0 || request.Layer >= document.Terrain.Layers.Length)
        {
            throw new ArgumentException($"The map has layers 0 to {document.Terrain.Layers.Length - 1}");
        }
        if (request.AlbedoScale is <= 0 || request.NormalScale is < 0)
        {
            throw new ArgumentException("A texture repeats over more than 0 ogrids");
        }
        string fields = string.Join(",", new (string, object?)[] { ("albedo", request.Albedo), ("normal", request.Normal), ("albedoScale", request.AlbedoScale), ("normalScale", request.NormalScale) }
            .Where(field => field.Item2 is not null).Select(field => field.Item1));
        string title = LayerTexturesViewModel.TitleOf(request.Layer, document.Terrain.Layers.Length);
        model.EditMap($"{title}: {fields}", d => d.UpdateLayer(request.Layer, l => l with
        {
            AlbedoPath = request.Albedo?.Trim() ?? l.AlbedoPath,
            NormalPath = request.Normal?.Trim() ?? l.NormalPath,
            AlbedoScale = request.AlbedoScale ?? l.AlbedoScale,
            NormalScale = request.NormalScale ?? l.NormalScale,
        }), $"paint.setLayer:{request.Layer}:{fields}");
        return Task.CompletedTask;
    }
}

/// <summary>
/// Gives the first unused stratum a texture (user story S-2), one step of undo: as many strata as
/// the <c>.scmap</c> version holds (four in version 53, eight after). With a person and without a
/// key, the picker asks for the texture.
/// </summary>
/// <param name="Key">The texture, as the shelf names it.</param>
/// <param name="Scale">How many ogrids it repeats over; 4 when left out.</param>
/// <returns>The new stratum's layer.</returns>
[EditorCommand("paint.addStratum", "Add a stratum", Story = "S-2", Needs = Needs.Map | Needs.NoStroke)]
public sealed record AddStratum(string? Key = null, float Scale = 4) : IRequest<int?>;

public sealed class AddStratumHandler(MainViewModel model, AssetsViewModel assets, IEditorDialogs dialogs) : IRequestHandler<AddStratum, int?>
{
    public async Task<int?> Handle(AddStratum request, CancellationToken cancellationToken)
    {
        MapDocument document = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
        Terrain terrain = document.Terrain;
        if (Enumerable.Range(1, terrain.Layers.Length - 2).All(layer => terrain.Layers[layer].IsUsed()))
        {
            throw new InvalidOperationException($"All {terrain.Layers.Length - 2} strata of a version {terrain.ScmapVersion} map are used: remove one first");
        }
        string? key = request.Key ?? await dialogs.PickAssetAsync(AssetKind.Texture, "Choose the texture of the new stratum");
        if (key is null)
        {
            model.Status = "Chose nothing";
            return null;
        }
        (string? albedo, string? normal) = LayerTexture.Of(assets, key);
        if (albedo is null)
        {
            throw new ArgumentException($"'{key}' is only a normal map: a stratum needs an albedo texture");
        }
        int added = 0;
        model.EditMap($"Add a stratum: {Path.GetFileNameWithoutExtension(albedo)}", d => d.AddStratum(albedo, normal ?? "", request.Scale, out added));
        model.LayerTextures.Choose(added);
        model.Status = $"Stratum {added} has the texture {key}; paint it with the Strata tool";
        return added;
    }
}

/// <summary>
/// Takes a stratum and its mask out (user story S-2), one step of undo; the strata above it move
/// down with their masks, so the map looks the same apart from it.
/// </summary>
/// <param name="Layer">The stratum, 1 to 8 (1 to 4 in version 53).</param>
[EditorCommand("paint.removeStratum", "Remove a stratum", Story = "S-2", Needs = Needs.Map | Needs.NoStroke)]
public sealed record RemoveStratum(int Layer) : IRequest;

public sealed class RemoveStratumHandler(MainViewModel model) : IRequestHandler<RemoveStratum>
{
    public Task Handle(RemoveStratum request, CancellationToken cancellationToken)
    {
        model.EditMap($"Remove stratum {request.Layer}", d => d.RemoveStratum(request.Layer));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Puts a stratum in another place among the strata, with its mask (user story S-2), one step of
/// undo: a later stratum covers an earlier one.
/// </summary>
/// <param name="Layer">The stratum to move.</param>
/// <param name="To">The place it takes; the ones between move one place.</param>
[EditorCommand("paint.moveStratum", "Move a stratum", Story = "S-2", Needs = Needs.Map | Needs.NoStroke)]
public sealed record MoveStratum(int Layer, int To) : IRequest;

public sealed class MoveStratumHandler(MainViewModel model) : IRequestHandler<MoveStratum>
{
    public Task Handle(MoveStratum request, CancellationToken cancellationToken)
    {
        model.EditMap($"Move stratum {request.Layer} to {request.To}", d => d.MoveStratum(request.Layer, request.To));
        model.LayerTextures.Choose(request.To);
        return Task.CompletedTask;
    }
}
