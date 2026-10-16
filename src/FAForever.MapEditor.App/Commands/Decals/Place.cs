using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Places a decal on the terrain centred on a place (user story D-1), one step of undo, and selects
/// it: what a click with the Decals mode's place tool and a decal dropped from the shelf do. What
/// is left out comes from the place tool's settings: its decal, width and turn, the distance
/// beyond which it is not drawn; its length follows the texture's shape.
/// </summary>
/// <param name="Key">The decal's texture, an asset of the workspace (<c>env/common/decals/erosion018_normals.dds</c>)
/// or a game path, or a decal set's name (<c>env/rocky/decals/rock01</c>: one decal carrying all its
/// textures); left out, the place tool's decal.</param>
/// <param name="X">The decal's middle, in ogrids.</param>
/// <param name="Width">Along the decal's own x axis, in ogrids.</param>
/// <param name="Heading">In degrees, clockwise from +x in the top view.</param>
/// <param name="CutOff">The distance beyond which the game does not draw it.</param>
/// <returns>The new decal, as a reference (<c>decal:12</c>).</returns>
[UndoStep]
[EditorCommand("decals.place", "Place a decal", Story = "D-1")]
public sealed record PlaceDecal(float X, float Z, string? Key = null, float? Width = null, float? Length = null, float? Heading = null, float? CutOff = null) : IRequest<string>;

public sealed class PlaceDecalHandler(MainViewModel model, SelectionViewModel selection, CurrentEdit edit) : IRequestHandler<PlaceDecal, string>
{
    public Task<string> Handle(PlaceDecal request, CancellationToken cancellationToken)
    {
        DecalPlaceTool tool = model.DecalPlaceTool;
        string key = request.Key ?? tool.Decal
            ?? throw new InvalidOperationException("Choose a decal to place first (decals.choose), or give its key");
        if (model.Assets.Find(key) is AssetEntry entry && entry.Kind != AssetKind.Decal)
        {
            throw new ArgumentException($"'{key}' is a {AssetsViewModel.Noun(entry.Kind, 1).ToLowerInvariant()}, not a decal");
        }
        if (model.ShownDocument is MapDocument shown && !DecalPlaceTool.IsOnMap(shown, request.X, request.Z))
        {
            throw new ArgumentOutOfRangeException(nameof(request.X), $"{request.X}, {request.Z} lies outside the map: a decal's middle lies on it");
        }
        ObjectRef added = default;
        System.Collections.Immutable.ImmutableArray<ObjectRef> all = [];
        bool set = false;
        edit.Transaction.Apply(document =>
        {
            Decal decal = tool.Make(document, key, request.X, request.Z, request.Width, request.Length,
                request.Heading is float degrees ? degrees * MathF.PI / 180 : null, request.CutOff);
            set = decal.Set is not null;
            document = document.PlaceDecal(decal, out added);
            // with the symmetry, its image in every part of the map (F-12)
            return model.MirrorPlaced(document, [added], out all);
        });
        selection.Set(all);
        model.Status = $"Placed {Path.GetFileNameWithoutExtension(key)}{(set ? " (a decal set)" : "")}{MainViewModel.Mirrored(all.Length)}";
        return Task.FromResult(added.ToString());
    }
}
