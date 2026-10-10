using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Places a prop at a place (user story P-1), one step of undo, standing on the terrain as the
/// Props panel's Placing says (alignment, random turn, tilt and scale, sink), and selects it: what
/// a click of the place tool and a prop dropped from the shelf do.
/// </summary>
/// <param name="Key">The prop: a blueprint's game path; left out, the place tool's.</param>
/// <param name="X">In ogrids.</param>
/// <param name="Heading">In degrees, clockwise from +x in the top view; left out, at random when
/// Placing turns at random, else 0.</param>
/// <returns>The new prop, as <c>prop:12</c>.</returns>
[UndoStep]
[EditorCommand("props.place", "Place a prop", Story = "P-1", Needs = Needs.Map)]
public sealed record PlaceProp(float X, float Z, string? Key = null, float? Heading = null) : IRequest<string>;

public sealed class PlacePropHandler(MainViewModel model, CurrentEdit edit) : IRequestHandler<PlaceProp, string>
{
    public Task<string> Handle(PlaceProp request, CancellationToken cancellationToken)
    {
        PropPlaceTool tool = model.Tool<PropPlaceTool>();
        string blueprint;
        if (request.Key is string key)
        {
            AssetEntry? entry = model.Assets.Catalog.Find(key);
            if (entry is not null && entry.Kind != AssetKind.Prop)
            {
                throw new ArgumentException($"'{key}' is no prop");
            }
            // a blueprint the workspace has not converted is placed all the same: the map may name it
            blueprint = entry is null ? "/" + key.TrimStart('/') : PropsViewModel.BlueprintOf(entry);
        }
        else
        {
            AssetEntry entry = tool.Prop ?? throw new InvalidOperationException("Choose a prop to place first: Choose..., Ctrl+P, or a prop on the shelf");
            blueprint = PropsViewModel.BlueprintOf(entry);
        }
        MapDocument document = edit.Transaction.Document;
        if (request.X < 0 || request.Z < 0 || request.X > document.Width || request.Z > document.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(request.X), $"{request.X}, {request.Z} lies outside the map");
        }
        Prop prop = model.Props.Placement.Make(blueprint, request.X, request.Z, document.Terrain.Heightmap, model.Props.Random,
            request.Heading is float degrees ? degrees * MathF.PI / 180 : null);
        int id = 0;
        System.Collections.Immutable.ImmutableArray<ObjectRef> all = [];
        edit.Transaction.Apply(current =>
        {
            MapDocument added = current.AddProp(prop);
            id = added.Props[^1].Id;
            // with the symmetry, its mirror image in every part of the map (F-12)
            return model.MirrorPlaced(added, [ObjectRef.Prop(id)], out all);
        });
        ObjectRef placed = ObjectRef.Prop(id);
        model.Selection.Set(all);
        model.Status = $"Placed {Path.GetFileNameWithoutExtension(blueprint)} at {request.X:0.#}, {request.Z:0.#}";
        return Task.FromResult(placed.ToString());
    }
}

/// <summary>
/// Chooses the prop the place tool places (user story P-1) in the picker and chooses the place
/// tool. Run without a person, the key is the argument.
/// </summary>
/// <param name="Key">A prop's blueprint path, to choose without asking.</param>
/// <returns>The chosen prop's key; null when the picker was cancelled.</returns>
[EditorCommand("props.choose", "Choose a prop to place...", Story = "P-1")]
public sealed record ChooseProp(string? Key = null) : IRequest<string?>;

public sealed class ChoosePropHandler(MainViewModel model, AssetsViewModel assets, IEditorDialogs dialogs) : IRequestHandler<ChooseProp, string?>
{
    public async Task<string?> Handle(ChooseProp request, CancellationToken cancellationToken)
    {
        PropPlaceTool tool = model.Tool<PropPlaceTool>();
        string? chosen = request.Key ?? await dialogs.PickAssetAsync(AssetKind.Prop, "Choose a prop to place", tool.Prop?.Key);
        if (chosen is null)
        {
            model.Status = "Chose nothing";
            return null;
        }
        AssetEntry entry = assets.Catalog.Find(chosen) is { Kind: AssetKind.Prop } found
            ? found
            : throw new ArgumentException($"The workspace has no prop '{chosen}'");
        tool.Prop = entry;
        model.SelectTool("props.place");
        model.Status = $"Placing {entry.Name}: click on the map";
        return entry.Key;
    }
}
