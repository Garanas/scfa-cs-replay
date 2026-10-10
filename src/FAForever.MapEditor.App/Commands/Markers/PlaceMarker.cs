using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Places a marker at a place on the terrain (user stories R-1, R-3), one step of undo, and selects
/// it: what a click with a placing tool of the Markers mode does. Its name is the next free one of
/// its kind (<c>Mass 12</c>, <c>Rally Point 03</c>, <c>LandPN7</c>); a spawn is the next playable
/// army (<c>ARMY_n</c>), added to the armies. Deposits go to the centre of their ogrid, where the
/// game puts them, and say so when the ground is too steep for the storages around an extractor.
/// </summary>
/// <param name="Kind"><c>spawn</c>, <c>mass</c>, <c>hydrocarbon</c>, <c>ai</c> or <c>path</c>.</param>
/// <param name="X">In ogrids.</param>
/// <param name="Type">An AI marker's type, e.g. <c>Rally Point</c> (left out: that one).</param>
/// <param name="Layer">A path node's layer: <c>Land</c> (left out), <c>Water</c>, <c>Amphibious</c>, <c>Air</c>.</param>
/// <param name="LinkTo">A path node to link the new one with, both ways: the path goes on.</param>
/// <returns>The marker placed: its reference, name and place, and a warning or null.</returns>
[UndoStep]
[EditorCommand("markers.place", "Place a marker", Story = "R-1", Needs = Needs.Map)]
public sealed record PlaceMarker(string Kind, float X, float Z, string? Type = null, string? Layer = null, int? LinkTo = null) : IRequest<JsonObject>;

public sealed class PlaceMarkerHandler(MainViewModel model, MarkersViewModel markers, SelectionViewModel selection, CurrentEdit edit) : IRequestHandler<PlaceMarker, JsonObject>
{
    public Task<JsonObject> Handle(PlaceMarker request, CancellationToken cancellationToken)
    {
        MapDocument document = edit.Transaction.Document;
        if (!float.IsFinite(request.X) || !float.IsFinite(request.Z) || request.X < 0 || request.Z < 0 || request.X > document.Width || request.Z > document.Height)
        {
            throw new ArgumentException($"{request.X.ToString(CultureInfo.InvariantCulture)}, {request.Z.ToString(CultureInfo.InvariantCulture)} lies outside the map ({document.Width} by {document.Height} ogrids)");
        }
        string kind = request.Kind.ToLowerInvariant();
        bool deposit = kind is "mass" or "hydrocarbon";
        float x = deposit ? MarkerEdits.Snap(request.X, MarkerSnap.Centre) : request.X;
        float z = deposit ? MarkerEdits.Snap(request.Z, MarkerSnap.Centre) : request.Z;
        Vector3 position = new Vector3(x, document.Terrain.Heightmap.HeightAt(x, z), z);
        PathLayer layer = request.Layer is null ? PathLayer.Land
            : Enum.TryParse(request.Layer, ignoreCase: true, out PathLayer parsed) && Enum.IsDefined(parsed) ? parsed
            : throw new ArgumentException($"There is no layer '{request.Layer}': Land, Water, Amphibious or Air");
        string type = request.Type ?? "Rally Point";
        Marker marker = kind switch
        {
            "spawn" => new Spawn { Id = 0, Name = "", Position = position },
            "mass" => new ResourceMarker { Id = 0, Name = "Mass 01", Kind = ResourceKind.Mass, Position = position },
            "hydrocarbon" => new ResourceMarker { Id = 0, Name = "Hydrocarbon 01", Kind = ResourceKind.Hydrocarbon, Position = position },
            "ai" => new AiMarker { Id = 0, Name = $"{type} 01", Type = type, Position = position },
            "path" => new PathNode { Id = 0, Name = $"{LayerPrefix(layer)}PN1", Layer = layer, Position = position },
            _ => throw new ArgumentException($"There is no kind of marker '{request.Kind}' to place: spawn, mass, hydrocarbon, ai or path"),
        };

        document = document.AddMarker(marker, out Marker added);
        if (request.LinkTo is int other)
        {
            if (added is not PathNode)
            {
                throw new ArgumentException("Only a path node links to another");
            }
            document = document.LinkPathNodes(added.Id, other);
        }
        edit.Transaction.Apply(_ => document);
        selection.Set([ObjectRef.Marker(added.Id)]);

        string? warning = null;
        if (added is ResourceMarker resource)
        {
            if (MarkerEdits.Steepness(document.Terrain.Heightmap, resource.Kind, x, z) is float steep)
            {
                warning = SteepWarning(resource, steep);
                model.Logger("Editor").LogInformation("{Marker} placed on steep ground, rising {Variation} over its footprint", added.Name, MathF.Round(steep, 1));
            }
            markers.PlacementWarning = warning;
        }
        model.Status = warning ?? $"Placed {added.Name}";
        return Task.FromResult(new JsonObject
        {
            ["ref"] = ObjectRef.Marker(added.Id).ToString(),
            ["name"] = added.Name,
            ["x"] = x,
            ["z"] = z,
            ["warning"] = warning,
        });
    }

    /// <summary>
    /// What the editor says about a deposit on steep ground.
    /// </summary>
    public static string SteepWarning(ResourceMarker deposit, float steepness) => string.Create(CultureInfo.InvariantCulture,
        $"{deposit.Name} lies on steep ground: it rises {steepness:0.#} over {MarkerEdits.DepositSize(deposit.Kind)} ogrids, more than the {MarkerEdits.MaxGroundVariation} the storages around an extractor allow");

    /// <summary>
    /// How the map editors begin the names of a layer's path nodes.
    /// </summary>
    private static string LayerPrefix(PathLayer layer) => layer switch
    {
        PathLayer.Amphibious => "Amph",
        _ => layer.ToString(),
    };
}
