using System.Collections.Immutable;
using System.Text.Json.Nodes;
using FAForever.FileFormats.Blueprints;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Puts the selected units into a new group (user story U-11), one step of undo: under the deepest
/// group that holds them all, named <c>GROUP_n</c> or the name given; units of several armies get a
/// group each, with the same name. The first new group is chosen in the Units mode's tree.
/// </summary>
/// <param name="Name">Left out, <c>GROUP_1</c>; a name a sibling has gets the next free number.</param>
/// <returns>The groups made: <c>groups</c>, each with its <c>army</c> and <c>group</c> path, and how many units went in.</returns>
[UndoStep]
[EditorCommand("units.group", "Group the selected units", Story = "U-11", Needs = Needs.Selection, Gesture = "Ctrl+G")]
public sealed record GroupSelectedUnits(string? Name = null) : IRequest<JsonObject>;

public sealed class GroupSelectedUnitsHandler(MainViewModel model, ArmiesViewModel armies, SelectionViewModel selection, CurrentEdit edit) : IRequestHandler<GroupSelectedUnits, JsonObject>
{
    public Task<JsonObject> Handle(GroupSelectedUnits request, CancellationToken cancellationToken)
    {
        string[] names = UnitCommands.SelectedUnits(selection);
        ImmutableArray<(string Army, ImmutableArray<string> Path)> groups = [];
        UnitCommands.Apply(edit, d => d.GroupUnits(names, request.Name, out groups));
        armies.ChooseWhenShown(groups[0].Army, groups[0].Path);
        string where = string.Join(", ", groups.Select(g => $"{UnitGroupEdits.PathText(g.Path)} of {g.Army}"));
        model.Status = $"Grouped {MainViewModel.Units(names.Length)} into {where}";
        return Task.FromResult(new JsonObject
        {
            ["count"] = names.Length,
            ["groups"] = new JsonArray([.. groups.Select(g => (JsonNode)new JsonObject { ["army"] = g.Army, ["group"] = UnitGroupEdits.PathText(g.Path) })]),
        });
    }
}

/// <summary>
/// Dissolves a group of units (user story U-11), one step of undo: its units and groups go into the
/// group above it and the group is removed (<c>units.removeGroup</c> with the units kept). Left out,
/// the group is the deepest one that holds all the selected units, else the group chosen in the
/// Units mode's tree. The armies' <c>INITIAL</c> and <c>WRECKAGE</c> groups are the game's and stay.
/// </summary>
/// <param name="Group">The group's path in the army: <c>INITIAL/Town</c>.</param>
/// <returns>The army, the group removed and how many units were in it.</returns>
[UndoStep]
[EditorCommand("units.ungroup", "Ungroup", Story = "U-11", Needs = Needs.Map)]
public sealed record UngroupUnits(string? Army = null, string? Group = null) : IRequest<JsonObject>;

public sealed class UngroupUnitsHandler(MainViewModel model, ArmiesViewModel armies, SelectionViewModel selection, CurrentEdit edit) : IRequestHandler<UngroupUnits, JsonObject>
{
    public Task<JsonObject> Handle(UngroupUnits request, CancellationToken cancellationToken)
    {
        MapDocument document = edit.Transaction.Document;
        (string army, ImmutableArray<string> path) = Which(document, request);
        UnitCommands.RequireArmy(document, army);
        UnitGroup group = document.FindGroup(army, path) ?? throw new ArgumentException($"The army {army} has no group {UnitGroupEdits.PathText(path)}");
        if (path.Length == 0)
        {
            throw new ArgumentException($"The root group of {army} cannot be ungrouped");
        }
        if (path.Length == 1 && path[0] is UnitGroupEdits.InitialGroup or UnitWrecks.GroupName)
        {
            throw new ArgumentException($"{path[0]} is a group of the game, whose units it creates when it starts; it stays");
        }
        int count = UnitGroupEdits.CountUnits(group);
        UnitCommands.Apply(edit, d => d.RemoveGroup(army, path, keepUnits: true));
        ImmutableArray<string> above = [.. path.Take(path.Length - 1)];
        armies.ChooseWhenShown(army, above);
        model.Status = $"Ungrouped {UnitGroupEdits.PathText(path)} of {army}: {MainViewModel.Units(count)} in {(above.IsEmpty ? "the root group" : UnitGroupEdits.PathText(above))} now";
        return Task.FromResult(new JsonObject { ["army"] = army, ["group"] = UnitGroupEdits.PathText(path), ["count"] = count });
    }

    private (string Army, ImmutableArray<string> Path) Which(MapDocument document, UngroupUnits request)
    {
        if (request.Army is not null || request.Group is not null)
        {
            string army = request.Army ?? armies.Chosen?.Army ?? throw new ArgumentException("Give the argument army with the group");
            return (army, UnitGroupEdits.ParsePath(request.Group));
        }
        PlacedUnit[] units = [.. selection.Items.Where(thing => thing.Kind == ObjectKind.Unit).Select(thing => document.FindUnit(thing.Name!)).OfType<PlacedUnit>()];
        if (units.Length > 0 && units.Select(unit => unit.Army).Distinct().Count() == 1)
        {
            ImmutableArray<string> common = units[0].Group;
            foreach (PlacedUnit unit in units.Skip(1))
            {
                int same = 0;
                while (same < common.Length && same < unit.Group.Length && common[same] == unit.Group[same])
                {
                    same++;
                }
                common = [.. common.Take(same)];
            }
            return (units[0].Army, common);
        }
        return armies.Chosen is { } chosen ? chosen : throw new ArgumentException("Select units in a group, or choose a group in the Units mode's tree, or give the arguments army and group");
    }
}

/// <summary>
/// Gives every selected unit another blueprint, chosen in the picker (user story U-11): the name,
/// place, heading, army and group stay; a structure keeps its centre on the build grid of its new
/// footprint. One step of undo. Run without a person, the blueprint is the argument.
/// </summary>
/// <param name="Blueprint">The unit's id to give them (<c>ueb1105</c>).</param>
/// <returns>How many units changed.</returns>
[UndoStep]
[EditorCommand("units.replace", "Replace the units' blueprint...", Story = "U-11", Needs = Needs.Selection)]
public sealed record ReplaceUnits(string? Blueprint = null) : IRequest<int>;

public sealed class ReplaceUnitsHandler(MainViewModel model, SelectionViewModel selection, IEditorDialogs dialogs, CurrentEdit edit) : IRequestHandler<ReplaceUnits, int>
{
    public async Task<int> Handle(ReplaceUnits request, CancellationToken cancellationToken)
    {
        string[] names = UnitCommands.SelectedUnits(selection);
        string? current = edit.Transaction.Document.FindUnit(names[0])?.Unit.Blueprint;
        string? chosen = request.Blueprint ?? await dialogs.PickAssetAsync(AssetKind.Unit, "Replace the units with", current);
        if (chosen is null)
        {
            model.Status = "Chose nothing";
            return 0;
        }
        AssetEntry entry = model.Assets.Catalog.Find(chosen) is { Kind: AssetKind.Unit } found
            ? found
            : throw new ArgumentException($"The workspace has no unit '{chosen}'");
        UnitReplacement replacement = UnitKinds.ReplacementFor(entry);
        int changed = 0;
        UnitCommands.Apply(edit, d => d.ReplaceUnits(names, _ => replacement, out changed));
        model.Status = changed == 0 ? $"The units are {entry.Name} already" : $"{MainViewModel.Units(changed)} {(changed == 1 ? "is" : "are")} {entry.Name} now";
        return changed;
    }
}

/// <summary>
/// Makes each selected unit the same kind of unit of another faction (user story U-11), one step of
/// undo: <c>ueb1101</c> becomes <c>urb1101</c> for the Cybran; where the catalogue has no such id the
/// unit of that faction with the same layer and tech level that shares most categories
/// (<see cref="UnitConversion"/>). A unit with no counterpart stays as it is, and is counted.
/// </summary>
/// <param name="Faction"><c>uef</c>, <c>cybran</c>, <c>aeon</c> or <c>seraphim</c>.</param>
/// <returns><c>converted</c>, <c>left</c> (no counterpart) and <c>already</c> (of that faction).</returns>
[UndoStep]
[EditorCommand("units.toFaction", "Convert the units to a faction...", Story = "U-11", Needs = Needs.Selection)]
public sealed record ConvertUnitsToFaction(string Faction) : IRequest<JsonObject>;

public sealed class ConvertUnitsToFactionHandler(MainViewModel model, SelectionViewModel selection, CurrentEdit edit) : IRequestHandler<ConvertUnitsToFaction, JsonObject>
{
    public Task<JsonObject> Handle(ConvertUnitsToFaction request, CancellationToken cancellationToken)
    {
        string[] names = UnitCommands.SelectedUnits(selection);
        Faction faction = UnitKinds.ParseFaction(request.Faction);
        AssetCatalog catalog = model.Assets.Catalog;
        int left = 0;
        int already = 0;
        int converted = 0;
        UnitCommands.Apply(edit, d =>
        {
            left = 0;
            already = 0;
            return d.ReplaceUnits(names, placed =>
            {
                AssetEntry? counterpart = catalog.Find(placed.Unit.Blueprint) is { Kind: AssetKind.Unit } entry ? UnitConversion.Counterpart(entry, faction, catalog) : null;
                if (counterpart is null)
                {
                    left++;
                    return null;
                }
                if (string.Equals(counterpart.Key, placed.Unit.Blueprint, StringComparison.OrdinalIgnoreCase))
                {
                    already++;
                    return null;
                }
                return UnitKinds.ReplacementFor(counterpart);
            }, out converted);
        });
        string name = faction.DisplayName();
        model.Status = $"Converted {MainViewModel.Units(converted)} to {name}"
            + (left > 0 ? $"; {left} left as {(left == 1 ? "it is" : "they are")}: no {name} counterpart" : "")
            + (already > 0 ? $"; {already} {(already == 1 ? "is" : "are")} {name} already" : "");
        return Task.FromResult(new JsonObject { ["converted"] = converted, ["left"] = left, ["already"] = already });
    }
}

/// <summary>
/// Spreads the selected units evenly along x or z between the first and the last of them along it
/// (user story U-11), one step of undo: they keep their order, the two at the ends stay, and a
/// structure stands on the build grid. At least three units.
/// </summary>
/// <param name="Axis"><c>x</c> or <c>z</c>; left out, the longer extent of the units.</param>
/// <returns>The axis and how many units moved.</returns>
[UndoStep]
[EditorCommand("units.distribute", "Distribute the units evenly", Story = "U-11", Needs = Needs.Selection)]
public sealed record DistributeUnits(string? Axis = null) : IRequest<JsonObject>;

public sealed class DistributeUnitsHandler(MainViewModel model, SelectionViewModel selection, CurrentEdit edit) : IRequestHandler<DistributeUnits, JsonObject>
{
    public Task<JsonObject> Handle(DistributeUnits request, CancellationToken cancellationToken)
    {
        string[] names = UnitCommands.SelectedUnits(selection);
        if (names.Length < 3)
        {
            throw new ArgumentException("Select at least 3 units to distribute");
        }
        bool? alongX = request.Axis?.ToLowerInvariant() switch
        {
            null or "" => null,
            "x" => true,
            "z" => false,
            _ => throw new ArgumentException($"An axis is x or z, not {request.Axis}"),
        };
        Func<string, (double, double)?> footprint = UnitKinds.StructureFootprint(model.Assets.Catalog);
        MapDocument document = edit.Transaction.Document;
        bool x = alongX ?? UnitEdits.LongerAlongX(document, names);
        int moved = 0;
        UnitCommands.Apply(edit, d => d.DistributeUnits(names, x, f => footprint(f), out moved));
        model.Status = $"Spread {MainViewModel.Units(names.Length)} evenly along {(x ? "x" : "z")}; {moved} moved";
        return Task.FromResult(new JsonObject { ["axis"] = x ? "x" : "z", ["moved"] = moved });
    }
}

/// <summary>
/// What the commands of units know of the catalogue's units: which blueprints are structures, what
/// replaces a unit, a faction by its name.
/// </summary>
internal static class UnitKinds
{
    /// <summary>
    /// What a unit becomes when it is replaced by an entry: its id, and its footprint when it is a
    /// structure (1 by 1 without one).
    /// </summary>
    public static UnitReplacement ReplacementFor(AssetEntry entry) => new UnitReplacement(entry.Key, entry.IsStructure ? entry.Footprint ?? (1, 1) : null);

    /// <summary>
    /// A blueprint's footprint when the catalogue knows it as a structure (1 by 1 without one); null for any other unit and for a blueprint it lacks.
    /// </summary>
    public static Func<string, (double SizeX, double SizeZ)?> StructureFootprint(AssetCatalog catalog) =>
        blueprint => catalog.Find(blueprint) is { IsStructure: true } entry ? entry.Footprint ?? (1, 1) : null;

    /// <exception cref="ArgumentException">It is none of the four factions.</exception>
    public static Faction ParseFaction(string name) => name.Trim().ToLowerInvariant() switch
    {
        "uef" => Faction.Uef,
        "cybran" => Faction.Cybran,
        "aeon" => Faction.Aeon,
        "seraphim" => Faction.Seraphim,
        _ => throw new ArgumentException($"A faction is uef, cybran, aeon or seraphim, not {name}"),
    };
}
