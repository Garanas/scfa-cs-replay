using System.Collections.Immutable;
using System.Globalization;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Places decals along a height curve (user story T-4, D2), once, as ordinary decals: one step of
/// undo, and they (with their images, for a curve drawn by a symmetry) become the selection. Each
/// lies centred on its place, its length along the curve. What is left out comes from the curve
/// inspector's "Decals along the curve" (<see cref="MainViewModel.DecalsAlong"/>).
/// </summary>
/// <param name="Layer">The curve layer; left out, the chosen height layer when it is a curve, else
/// the topmost curve.</param>
/// <param name="Key">The decal (an asset's key or a game path) or a decal set's name; left out, the
/// picker asks (as <c>decals.choose</c>).</param>
/// <param name="Spacing">Between two decals' middles along the curve, in ogrids; left out, the
/// section's (0 there: the decals' length, end to end).</param>
/// <param name="Width">Each decal's width across the curve, in ogrids.</param>
/// <param name="Length">Each decal's length along the curve; left out, the width times the
/// texture's shape.</param>
/// <param name="Offset">How far beside the curve, in ogrids: positive on its left (walking from the
/// first point), negative on its right.</param>
/// <param name="Alternate">Every other decal on the other side.</param>
/// <param name="RandomTurn">Up to how many degrees each is turned either way, at random.</param>
/// <param name="Jitter">Up to how many ogrids each is moved along and across, at random.</param>
/// <param name="Seed">The seed of the turn and jitter.</param>
/// <param name="CutOff">The distance beyond which the game does not draw them; left out, the place
/// tool's.</param>
/// <returns>How many decals were placed, images included.</returns>
[UndoStep]
[EditorCommand("decals.alongCurve", "Place decals along a curve...", Story = "T-4", Needs = Needs.Map | Needs.NoStroke)]
public sealed record PlaceDecalsAlongCurve(string? Layer = null, string? Key = null, float? Spacing = null, float? Width = null, float? Length = null,
    float? Offset = null, bool? Alternate = null, float? RandomTurn = null, float? Jitter = null, int? Seed = null, float? CutOff = null) : IRequest<int>;

public sealed class PlaceDecalsAlongCurveHandler(MainViewModel model, AssetsViewModel assets, IEditorDialogs dialogs, SelectionViewModel selection, CurrentEdit edit)
    : IRequestHandler<PlaceDecalsAlongCurve, int>
{
    public async Task<int> Handle(PlaceDecalsAlongCurve request, CancellationToken cancellationToken)
    {
        MapDocument shown = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
        Layer layer = CurveLayer(model, shown, request.Layer);
        HeightCurve curve = layer.Curve!;
        if (request.Spacing is <= 0 || request.Width is <= 0 || request.Length is <= 0)
        {
            throw new ArgumentException("The spacing, width and length of decals along a curve are more than 0 ogrids");
        }
        DecalsAlongCurveViewModel section = model.DecalsAlong;
        string? key = await PickDecalAsync(model, assets, dialogs, request.Key);
        if (key is null)
        {
            return 0;
        }
        section.Decal = key;
        DecalPlaceTool tool = model.DecalPlaceTool;
        float width = request.Width ?? (float)section.Width;
        float length = request.Length ?? width * AspectOf(key);
        float? spacing = request.Spacing ?? (section.Spacing > 0 ? (float)section.Spacing : null);
        DecalsAlongCurve settings = new DecalsAlongCurve
        {
            Spacing = spacing,
            Width = width,
            Length = length,
            Offset = request.Offset ?? (float)section.Offset,
            Alternate = request.Alternate ?? section.Alternate,
            RandomTurn = request.RandomTurn ?? 0,
            Jitter = request.Jitter ?? 0,
            Seed = request.Seed ?? 0,
            CutOff = request.CutOff ?? (float)tool.CutOffLod,
        };
        ImmutableArray<ObjectRef> added = [];
        using (Performance.Measure("model.decals-along-curve"))
        {
            edit.Transaction.Apply(document =>
            {
                Decal pattern = tool.Make(document, key, 0, 0, width, length, 0, settings.CutOff);
                return document.AlongCurve(curve, pattern, settings, out added);
            });
        }
        selection.Set(added);
        string name = Path.GetFileNameWithoutExtension(key);
        if (added.IsEmpty)
        {
            model.Logger("Editor").LogWarning("No decal along the curve {Layer} lies on the map", layer.Name);
            model.Status = $"No decal along the curve {layer.Name} lies on the map";
            return 0;
        }
        model.Logger("Editor").LogDebug("Placed {Count} decals {Decal} along the curve {Layer}", added.Length, name, layer.Name);
        model.Status = string.Create(CultureInfo.InvariantCulture,
            $"Placed {added.Length} decal{(added.Length == 1 ? "" : "s")} {name} along the curve {layer.Name}{(curve.Symmetry is null ? "" : ", its images included")}");
        return added.Length;

        float AspectOf(string decal)
        {
            if (decal == tool.Decal)
            {
                return tool.Aspect;
            }
            AssetEntry? texture = assets.Find(decal)
                ?? (DecalSets.Find(assets.AllEntries, shown, decal)?.First is string first ? assets.Find(first) : null);
            return texture?.Image is string image ? DecalPlaceTool.AspectOf(image, model.Logger("Assets")) : 1;
        }
    }

    /// <summary>
    /// The key given, else the decal the person picks (starting on the section's), as a decal's key
    /// or a decal set's name. Unlike <c>decals.choose</c> it leaves the shelf and the inspector as
    /// they are, so the curve stays in the inspector.
    /// </summary>
    /// <exception cref="ArgumentException">The key names an asset that is not a decal.</exception>
    internal static async Task<string?> PickDecalAsync(MainViewModel model, AssetsViewModel assets, IEditorDialogs dialogs, string? key)
    {
        string? chosen = key ?? await dialogs.PickAssetAsync(AssetKind.Decal, "Choose the decal to place along the curve", model.DecalsAlong.Decal);
        if (chosen is null)
        {
            model.Status = "Chose nothing";
            return null;
        }
        if (assets.Find(chosen) is AssetEntry entry)
        {
            if (entry.Kind != AssetKind.Decal)
            {
                throw new ArgumentException($"'{chosen}' is a {AssetsViewModel.Noun(entry.Kind, 1).ToLowerInvariant()}, not a decal");
            }
            assets.NoteUsed(entry.Key);
            return entry.Key;
        }
        if (DecalSets.Find(assets.AllEntries, model.ShownDocument, chosen) is DecalSetAssets set)
        {
            assets.NoteUsed(set.First ?? set.Name);
            return set.Name;
        }
        // a game path the workspace does not have: placed as it names it, as decals.place does
        return chosen;
    }

    /// <summary>
    /// The curve layer named, else the chosen height layer when it is a curve, else the topmost curve
    /// of the heightmap shown.
    /// </summary>
    /// <exception cref="ArgumentException">There is no such curve.</exception>
    internal static Layer CurveLayer(MainViewModel model, MapDocument document, string? name)
    {
        if (name is not null || HeightLayers.Find(document, model.HeightStack.Chosen)?.Curve is not null)
        {
            Layer named = HeightLayerArgument.Of(model, name);
            return named.Curve is null ? throw new ArgumentException($"The height layer {named.Name} is no curve") : named;
        }
        return HeightStacks.Of(document, model.ShownHeights).Layers.LastOrDefault(layer => layer.Curve is not null)
            ?? throw new ArgumentException("The map has no curve: draw one with the curve tool (Terrain mode) first");
    }
}

/// <summary>
/// Chooses the decal that "Decals along the curve" places (user story T-4, D2), with the picker; run
/// without a person, the key is the argument.
/// </summary>
/// <param name="Key">The decal's key or a decal set's name.</param>
/// <returns>The chosen key; null when the picker was cancelled.</returns>
[EditorCommand("decals.chooseAlongCurve", "Choose the decal to place along a curve...", Story = "T-4")]
public sealed record ChooseDecalAlongCurve(string? Key = null) : IRequest<string?>;

public sealed class ChooseDecalAlongCurveHandler(MainViewModel model, AssetsViewModel assets, IEditorDialogs dialogs) : IRequestHandler<ChooseDecalAlongCurve, string?>
{
    public async Task<string?> Handle(ChooseDecalAlongCurve request, CancellationToken cancellationToken)
    {
        string? key = await PlaceDecalsAlongCurveHandler.PickDecalAsync(model, assets, dialogs, request.Key);
        if (key is not null)
        {
            model.DecalsAlong.Decal = key;
            model.Status = $"Decals along a curve: {model.DecalsAlong.Title}";
        }
        return key;
    }
}
