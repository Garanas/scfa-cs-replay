using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json.Nodes;
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
/// Places many units at once (user story U-7), one step of undo: a line from one place to another
/// (structures butting together at their skirt, mobile units a spacing apart, each facing
/// along the line or the heading), a rectangle between two corners (filled, or hollow for walls),
/// a ring around the selected units (a structure's skirt) or around the structure at a place, or a scatter
/// of random places within a circle (random headings, none closer than the least distance). What
/// is left out is the place tool's: its mode, unit, army, group, heading and settings. A structure
/// stands on the build grid in quarter turns. With the symmetry, every unit's images are placed
/// too (a structure's image on the build grid); what was placed is selected. A unit that would
/// stand outside the map is left out (the ends of a line and the corners of a rectangle may lie
/// outside it; the other points must lie on the map). More than <see cref="AddManyUnits.AskAbove"/>
/// units are asked about first; without a person to ask, <paramref name="Confirm"/> must be true.
/// </summary>
/// <param name="Mode"><c>line</c>, <c>rectangle</c>, <c>ring</c> or <c>scatter</c>; left out, the place tool's (<c>single</c> places one unit at <c>x0, z0</c>).</param>
/// <param name="X0">A line's start, a rectangle's corner, in ogrids.</param>
/// <param name="X1">A line's end, the other corner.</param>
/// <param name="X">A ring's click (the structure under it; left out, the selection) or a scatter's centre.</param>
/// <param name="Points">Instead of the places above: <c>[[x, z], ...]</c>, the first and last of a line or rectangle, the click of a ring, each dab of a scatter.</param>
/// <param name="Blueprint">The unit's id (<c>ueb5101</c>); left out, the place tool's unit.</param>
/// <param name="Heading">In degrees, clockwise from +x in the top view; left out, the place tool's.</param>
/// <param name="Army">Left out, the place tool's army.</param>
/// <param name="Group">The group's path below the army's root group (<c>INITIAL/Walls</c>, made when missing; empty for the root group); left out, the place tool's.</param>
/// <param name="Wreck">Whether they are wrecks (user story U-5): into the army's <c>WRECKAGE</c> group; left out, as the place tool's As wrecks says.</param>
/// <param name="Spacing">Between mobile units, in ogrids.</param>
/// <param name="FaceAlong">Whether a line's units face along it.</param>
/// <param name="Hollow">Whether only a rectangle's border is filled.</param>
/// <param name="Distance">Between a ring and what it goes around, in ogrids.</param>
/// <param name="Count">How many a scatter puts in its circle.</param>
/// <param name="LeastDistance">How close scattered units may stand, in ogrids.</param>
/// <param name="Radius">A scatter's circle, in ogrids.</param>
/// <param name="Seed">A scatter's random seed, for the same places again; left out, a new one.</param>
/// <param name="Confirm">Yes to placing more than <see cref="AddManyUnits.AskAbove"/> units, when nobody can be asked.</param>
/// <returns>The units' names, how many (and how many images), the army, group, mode and how many places lay outside the map.</returns>
[UndoStep]
[EditorCommand("units.addMany", "Place many units", Story = "U-7")]
public sealed record AddManyUnits(string? Mode = null, float? X0 = null, float? Z0 = null, float? X1 = null, float? Z1 = null, float? X = null, float? Z = null,
    IReadOnlyList<IReadOnlyList<float>>? Points = null, string? Blueprint = null, float? Heading = null, string? Army = null, string? Group = null, bool? Wreck = null,
    float? Spacing = null, bool? FaceAlong = null, bool? Hollow = null, float? Distance = null, int? Count = null, float? LeastDistance = null, float? Radius = null, int? Seed = null, bool Confirm = false) : IRequest<JsonObject>
{
    /// <summary>
    /// More units than this in one go are asked about first: a slip of the mouse on a big map is
    /// one huge step.
    /// </summary>
    public const int AskAbove = 500;
}

public sealed class AddManyUnitsHandler(MainViewModel model, AssetsViewModel assets, SelectionViewModel selection, IEditorDialogs dialogs, CurrentEdit edit) : IRequestHandler<AddManyUnits, JsonObject>
{
    public async Task<JsonObject> Handle(AddManyUnits request, CancellationToken cancellationToken)
    {
        using Performance.Span measure = Performance.Measure("model.add-many-units");
        UnitPlaceTool tool = model.Tool<UnitPlaceTool>();
        MapDocument document = edit.Transaction.Document;
        PlaceMode mode = request.Mode is string name
            ? Enum.TryParse(name, ignoreCase: true, out PlaceMode parsed) ? parsed : throw new ArgumentException($"'{name}' is no way to place: single, line, rectangle, ring or scatter")
            : tool.Mode;
        (string id, AssetEntry? entry) = UnitCommands.ResolveUnit(request.Blueprint, tool, assets, model);
        string army = request.Army ?? tool.Army ?? throw new ArgumentException("The map has no army to place units for: add one in the Map mode");
        UnitCommands.RequireArmy(document, army);
        bool wreck = request.Wreck ?? tool.AsWrecks;
        ImmutableArray<string> group = wreck ? document.WreckagePath(army) : UnitGroupEdits.ParsePath(request.Group ?? tool.Group);
        float heading = (float)((request.Heading ?? tool.Heading) * Math.PI / 180);
        UnitLayoutSettings settings = new UnitLayoutSettings(
            request.Spacing ?? (float)tool.Spacing,
            request.FaceAlong ?? tool.FaceAlong,
            request.Hollow ?? tool.Hollow,
            request.Distance ?? (float)tool.Distance,
            heading);

        // the places: the points given, else the tool's mouse
        List<Vector2> points = [];
        if (request.Points is { Count: > 0 } list)
        {
            foreach (IReadOnlyList<float> point in list)
            {
                if (point.Count != 2)
                {
                    throw new ArgumentException("Each point is [x, z]");
                }
                points.Add(new Vector2(point[0], point[1]));
            }
        }
        else if (request.X0 is float x0 && request.Z0 is float z0)
        {
            points.Add(new Vector2(x0, z0));
            if (request.X1 is float x1 && request.Z1 is float z1)
            {
                points.Add(new Vector2(x1, z1));
            }
        }
        else if (request.X is float x && request.Z is float z)
        {
            points.Add(new Vector2(x, z));
        }
        // a line's ends and a rectangle's corners may lie beyond the edge: the units inside are placed
        foreach (Vector2 point in mode is PlaceMode.Line or PlaceMode.Rectangle ? (IEnumerable<Vector2>)[] : points)
        {
            if (point.X < 0 || point.Y < 0 || point.X > document.Width || point.Y > document.Height)
            {
                throw new ArgumentException(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{point.X:0.#}, {point.Y:0.#} lies outside the map"));
            }
        }

        ImmutableArray<UnitPlace> places;
        string how;
        switch (mode)
        {
            case PlaceMode.Line:
            case PlaceMode.Rectangle:
            {
                if (points.Count == 0)
                {
                    throw new ArgumentException($"Drag a {mode.ToString().ToLowerInvariant()} in a view, or give x0, z0, x1 and z1");
                }
                Vector2 from = points[0];
                Vector2 to = points[^1];
                places = UnitPlans.Of(mode, entry, from, to, null, settings);
                how = mode == PlaceMode.Line
                    ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"in a line of {Vector2.Distance(from, to):0.#} ogrids")
                    : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"in a{(settings.Hollow ? " hollow" : "")} rectangle of {MathF.Abs(to.X - from.X):0.#} by {MathF.Abs(to.Y - from.Y):0.#} ogrids");
                break;
            }
            case PlaceMode.Ring:
            {
                Vector2? at = points.Count > 0 ? points[0] : null;
                (float X0, float Z0, float X1, float Z1)? bounds = UnitPlans.RingBounds(document, assets.Catalog, selection.Items, at)
                    ?? throw new ArgumentException("Select units to place a ring around, or click a structure");
                places = UnitPlans.Of(PlaceMode.Ring, entry, default, default, bounds, settings);
                how = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"in a ring {settings.Distance:0.#} ogrids around {bounds.Value.X1 - bounds.Value.X0:0.#} by {bounds.Value.Z1 - bounds.Value.Z0:0.#} ogrids");
                break;
            }
            case PlaceMode.Scatter:
            {
                if (points.Count == 0)
                {
                    throw new ArgumentException("Give the centre of the scatter as x and z, or dabs as points");
                }
                float radius = request.Radius ?? (float)tool.Radius;
                int count = request.Count ?? tool.Count;
                float least = request.LeastDistance ?? (float)tool.LeastDistance;
                int seed = request.Seed ?? Random.Shared.Next();
                List<Vector2> scattered = [];
                ImmutableArray<UnitPlace>.Builder all = ImmutableArray.CreateBuilder<UnitPlace>();
                for (int i = 0; i < points.Count; i++)
                {
                    foreach (UnitPlace place in UnitLayouts.Scatter(points[i], radius, count, least, seed + i, scattered))
                    {
                        scattered.Add(new Vector2(place.X, place.Z));
                        all.Add(place);
                    }
                }
                places = all.ToImmutable();
                how = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"scattered within {radius:0.#} ogrids (seed {seed})");
                break;
            }
            default:
            {
                if (points.Count == 0)
                {
                    throw new ArgumentException("Give the place as x0 and z0");
                }
                places = [UnitPlans.Single(entry, points[0].X, points[0].Y, heading)];
                how = "";
                break;
            }
        }

        List<Project.Files.InMemory.Unit> units = [];
        int outside = 0;
        foreach (UnitPlace place in places)
        {
            Project.Files.InMemory.Unit unit = UnitCommands.Make(document, id, entry, place);
            if (unit.Position.X < 0 || unit.Position.Z < 0 || unit.Position.X > document.Width || unit.Position.Z > document.Height)
            {
                outside++;
                continue;
            }
            units.Add(unit);
        }
        if (units.Count == 0)
        {
            throw new ArgumentException(outside > 0 ? $"Every one of the {outside} places lies outside the map" : "Nothing to place there");
        }
        if (units.Count > AddManyUnits.AskAbove && !request.Confirm && !await dialogs.ConfirmAsync("Place many units",
            string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Place {units.Count:N0} units ({entry?.Name ?? id}) {how} as one step?"), "Place"))
        {
            throw new OperationCanceledException("Placing was cancelled");
        }

        ImmutableArray<string> names = [];
        ImmutableArray<ObjectRef> placed = [];
        UnitCommands.Apply(edit, d =>
        {
            d = d.AddUnits(army, group, units, out names);
            // with the symmetry, every unit's image in every part of the map, a structure on the build grid (F-12)
            return model.MirrorPlaced(d, [.. names.Select(ObjectRef.Unit)], out placed);
        });
        selection.Set(placed);
        string what = $"{MainViewModel.Units(names.Length)} ({entry?.Name ?? id})";
        edit.Transaction.Description = $"Place {what} {how}".TrimEnd();
        model.Status = $"Placed {what} {UnitCommands.Where(army, group, true)} {how}{MainViewModel.Mirrored(placed.Length - names.Length + 1)}{(outside > 0 ? $"; {outside} left out beyond the map's edge" : "")}".Replace("  ", " ");
        if (outside > 0)
        {
            model.Logger("Editor").LogInformation("{Outside} of the units to place lay outside the map and were left out", outside);
        }
        return new JsonObject
        {
            ["mode"] = mode.ToString(),
            ["count"] = names.Length,
            ["images"] = placed.Length - names.Length,
            ["units"] = new JsonArray([.. names.Select(name => (JsonNode)JsonValue.Create(name))]),
            ["blueprint"] = id,
            ["army"] = army,
            ["group"] = UnitGroupEdits.PathText(group),
            ["wreck"] = wreck,
            ["outside"] = outside,
        };
    }
}

/// <summary>
/// How the place tool places (user story U-7): one unit, a line, a rectangle, a ring or a
/// scatter, and the settings of each; chooses the Place tool of the Units mode. Without a mode,
/// the next way after the current one, so the menu steps through them.
/// </summary>
/// <param name="Mode"><c>single</c>, <c>line</c>, <c>rectangle</c>, <c>ring</c> or <c>scatter</c>.</param>
/// <param name="Spacing">Between mobile units, in ogrids.</param>
/// <param name="FaceAlong">Whether a line's units face along it.</param>
/// <param name="Hollow">Whether only a rectangle's border is filled.</param>
/// <param name="Distance">Between a ring and what it goes around, in ogrids.</param>
/// <param name="Count">How many a scatter puts in its circle.</param>
/// <param name="LeastDistance">How close scattered units may stand, in ogrids.</param>
/// <param name="Radius">The scatter brush's radius, in ogrids.</param>
/// <param name="Heading">What is placed faces this, in degrees.</param>
/// <returns>The tool's settings, as <c>state.units.place</c>.</returns>
[EditorCommand("units.placeMode", "Way to place units", Story = "U-7", Needs = Needs.Map)]
public sealed record SetPlaceMode(string? Mode = null, float? Spacing = null, bool? FaceAlong = null, bool? Hollow = null, float? Distance = null,
    int? Count = null, float? LeastDistance = null, float? Radius = null, float? Heading = null) : IRequest<JsonObject>;

public sealed class SetPlaceModeHandler(MainViewModel model) : IRequestHandler<SetPlaceMode, JsonObject>
{
    public Task<JsonObject> Handle(SetPlaceMode request, CancellationToken cancellationToken)
    {
        UnitPlaceTool tool = model.Tool<UnitPlaceTool>();
        PlaceMode mode = request.Mode is string name
            ? Enum.TryParse(name, ignoreCase: true, out PlaceMode parsed) ? parsed : throw new ArgumentException($"'{name}' is no way to place: single, line, rectangle, ring or scatter")
            : (PlaceMode)(((int)tool.Mode + 1) % Enum.GetValues<PlaceMode>().Length);
        if (request.Spacing is float spacing)
        {
            tool.Spacing = Math.Max(UnitLayouts.MinimumSpacing, spacing);
        }
        if (request.FaceAlong is bool faceAlong)
        {
            tool.FaceAlong = faceAlong;
        }
        if (request.Hollow is bool hollow)
        {
            tool.Hollow = hollow;
        }
        if (request.Distance is float distance)
        {
            tool.Distance = Math.Max(0, distance);
        }
        if (request.Count is int count)
        {
            tool.Count = Math.Max(1, count);
        }
        if (request.LeastDistance is float least)
        {
            tool.LeastDistance = Math.Max(0, least);
        }
        if (request.Radius is float radius)
        {
            tool.Radius = Math.Clamp(radius, BrushKeys.MinimumRadius, BrushKeys.MaximumRadius);
        }
        if (request.Heading is float heading)
        {
            tool.Heading = ((heading % 360) + 360) % 360;
        }
        model.SelectTool("units.place");
        tool.Mode = mode;
        model.Status = $"Place: {mode}. {UnitPlaceTool.HelpOf(mode)}";
        return Task.FromResult(tool.Describe());
    }
}
