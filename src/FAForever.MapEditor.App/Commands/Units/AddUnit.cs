using System.Collections.Immutable;
using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Places a unit on the map (user story U-2), one step of undo, named with a free <c>UNIT_n</c>:
/// what a click with the Units mode's place tool does. A structure turns in quarter turns and
/// snaps to the build grid by its footprint (an odd size on a half ogrid, an even one on a whole);
/// a mobile unit stands where it is put, turned as asked. What is left out is the place tool's:
/// its unit, army, group and heading.
/// </summary>
/// <param name="X">In ogrids.</param>
/// <param name="Blueprint">The unit's id (<c>uec1101</c>); left out, the place tool's unit.</param>
/// <param name="Heading">In degrees, clockwise from +x in the top view; left out, the place tool's.</param>
/// <param name="Army">Left out, the place tool's army.</param>
/// <param name="Group">The group's path below the army's root group (<c>INITIAL/Town</c>, made when
/// missing; empty for the root group); left out, the place tool's group.</param>
/// <param name="Snap">Whether a structure snaps to the build grid and to quarter turns.</param>
/// <param name="Wreck">Whether it is a wreck (user story U-5): it goes into the army's <c>WRECKAGE</c>
/// group in place of the group; left out, as the place tool's As wrecks says.</param>
/// <returns>The unit's name, blueprint, army, group, place and heading.</returns>
[UndoStep]
[EditorCommand("units.add", "Place a unit", Story = "U-2")]
public sealed record AddUnit(float X, float Z, string? Blueprint = null, float? Heading = null, string? Army = null, string? Group = null, bool Snap = true, bool? Wreck = null) : IRequest<JsonObject>;

public sealed class AddUnitHandler(MainViewModel model, AssetsViewModel assets, CurrentEdit edit) : IRequestHandler<AddUnit, JsonObject>
{
    public Task<JsonObject> Handle(AddUnit request, CancellationToken cancellationToken)
    {
        UnitPlaceTool tool = model.Tool<UnitPlaceTool>();
        MapDocument document = edit.Transaction.Document;
        (string id, AssetEntry? entry) = UnitCommands.ResolveUnit(request.Blueprint, tool, assets, model);
        string army = request.Army ?? tool.Army ?? throw new ArgumentException("The map has no army to place units for: add one in the Map mode");
        UnitCommands.RequireArmy(document, army);
        bool wreck = request.Wreck ?? tool.AsWrecks;
        ImmutableArray<string> group = wreck ? document.WreckagePath(army) : UnitGroupEdits.ParsePath(request.Group ?? tool.Group);

        float x = request.X;
        float z = request.Z;
        if (x < 0 || z < 0 || x > document.Width || z > document.Height)
        {
            throw new ArgumentException($"{x:0.#}, {z:0.#} lies outside the map");
        }
        float radians = (float)((request.Heading ?? tool.Heading) * Math.PI / 180);
        if (entry is { IsStructure: true } && request.Snap)
        {
            radians = BuildGrid.SnapHeading(radians);
            (double sizeX, double sizeZ) = entry.Footprint ?? (1, 1);
            (x, z) = BuildGrid.Snap(x, z, sizeX, sizeZ, radians);
        }
        radians = UnitCommands.Wrap(radians);

        string name = "";
        ImmutableArray<ObjectRef> all = [];
        UnitCommands.Apply(edit, d =>
        {
            name = d.NextUnitName();
            d = d.AddUnit(army, group, new Project.Files.InMemory.Unit
            {
                Name = name,
                Blueprint = id,
                Position = new System.Numerics.Vector3(x, d.Terrain.Heightmap.HeightAt(x, z), z),
                Orientation = new System.Numerics.Vector3(0, radians, 0),
            });
            // with the symmetry, its image in every part of the map, a structure on the build grid (F-12)
            return model.MirrorPlaced(d, [ObjectRef.Unit(name)], out all);
        });
        if (all.Length > 1)
        {
            model.Selection.Set(all);
        }
        model.Status = $"Placed {name} ({entry?.Name ?? id}) {UnitCommands.Where(army, group, false)}{MainViewModel.Mirrored(all.Length)}";
        return Task.FromResult(new JsonObject
        {
            ["name"] = name,
            ["blueprint"] = id,
            ["army"] = army,
            ["group"] = UnitGroupEdits.PathText(group),
            ["wreck"] = wreck,
            ["x"] = x,
            ["z"] = z,
            ["heading"] = Math.Round(SelectionInspector.Degrees(radians), 3),
        });
    }
}

/// <summary>
/// What the commands of the Units and Map modes share.
/// </summary>
internal static class UnitCommands
{
    /// <summary>
    /// Where placed units went, for the status line: <c>for ARMY_1</c> into the initial group, else
    /// <c>into ARMY_1's WRECKAGE: a wreck</c> (a group below a <c>WRECKAGE</c> group is wrecks).
    /// </summary>
    public static string Where(string army, ImmutableArray<string> group, bool plural)
    {
        if (group.IsEmpty || (group.Length == 1 && group[0] == UnitGroupEdits.InitialGroup))
        {
            return $"for {army}";
        }
        bool wrecks = group.Contains(UnitWrecks.GroupName, StringComparer.Ordinal);
        return $"into {army}'s {UnitGroupEdits.PathText(group)}{(wrecks ? (plural ? ": wrecks" : ": a wreck") : "")}";
    }

    /// <summary>
    /// Applies an edit of the library; a thing it does not find (an army, a group) is an argument
    /// that is wrong, which the status line shows.
    /// </summary>
    public static void Apply(CurrentEdit edit, Func<MapDocument, MapDocument> change)
    {
        try
        {
            edit.Transaction.Apply(change);
        }
        catch (KeyNotFoundException exception)
        {
            throw new ArgumentException(exception.Message, exception);
        }
    }

    /// <summary>
    /// The unit a placing command places: the blueprint given, else the place tool's unit, as the
    /// catalogue knows it (its id in lower case; a unit the workspace lacks is placed as it is, with
    /// a warning, and has no entry).
    /// </summary>
    /// <exception cref="ArgumentException">No unit is given or chosen, or the key is no unit.</exception>
    public static (string Id, AssetEntry? Entry) ResolveUnit(string? blueprint, UnitPlaceTool tool, AssetsViewModel assets, MainViewModel model)
    {
        string key = blueprint ?? tool.Unit?.Key
            ?? throw new ArgumentException("Choose a unit to place (Units, Choose a unit), or give its id as the argument blueprint");
        AssetEntry? entry = assets.Catalog.Find(key);
        if (entry is not null && entry.Kind != AssetKind.Unit)
        {
            throw new ArgumentException($"'{key}' is no unit");
        }
        string id = entry?.Key ?? key.Trim().ToLowerInvariant();
        if (entry is null)
        {
            model.Logger("Editor").LogWarning("The workspace has no unit {Blueprint}: placed as it is, without its footprint", id);
        }
        return (id, entry);
    }

    /// <summary>
    /// A unit standing on the terrain at a place: a structure on the build grid and in quarter
    /// turns, a mobile unit as the place says; named when it is added.
    /// </summary>
    public static Project.Files.InMemory.Unit Make(MapDocument document, string id, AssetEntry? entry, UnitPlace place)
    {
        float x = place.X;
        float z = place.Z;
        float heading = place.Heading;
        if (entry is { IsStructure: true })
        {
            heading = BuildGrid.SnapHeading(heading);
            (double sizeX, double sizeZ) = entry.Footprint ?? (1, 1);
            (x, z) = BuildGrid.Snap(x, z, sizeX, sizeZ, heading);
        }
        return new Project.Files.InMemory.Unit
        {
            Name = "",
            Blueprint = id,
            Position = new System.Numerics.Vector3(x, document.Terrain.Heightmap.HeightAt(Math.Clamp(x, 0, document.Width), Math.Clamp(z, 0, document.Height)), z),
            Orientation = new System.Numerics.Vector3(0, Wrap(heading), 0),
        };
    }

    /// <exception cref="ArgumentException">The map has no army of that name.</exception>
    public static void RequireArmy(MapDocument document, string army)
    {
        if (document.FindArmy(army) is null)
        {
            throw new ArgumentException($"The map has no army {army}: {string.Join(", ", document.Armies.Select(a => a.Name))}");
        }
    }

    /// <summary>
    /// The names of the selected units.
    /// </summary>
    /// <exception cref="ArgumentException">No unit is selected.</exception>
    public static string[] SelectedUnits(SelectionViewModel selection)
    {
        string[] names = [.. selection.Items.Where(thing => thing.Kind == ObjectKind.Unit).Select(thing => thing.Name!)];
        return names.Length > 0 ? names : throw new ArgumentException("Select units first");
    }

    /// <summary>
    /// An angle between -pi and pi.
    /// </summary>
    public static float Wrap(float radians)
    {
        float wrapped = MathF.IEEERemainder(radians, 2 * MathF.PI);
        return wrapped <= -MathF.PI ? wrapped + 2 * MathF.PI : wrapped;
    }
}
