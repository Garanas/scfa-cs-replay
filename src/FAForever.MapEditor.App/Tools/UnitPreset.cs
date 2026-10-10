using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json.Nodes;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Tools;

/// <summary>
/// A unit of a units preset: its blueprint, its place relative to the middle of the preset (x
/// and z in ogrids, y its height above the terrain) and its heading in radians.
/// </summary>
public sealed record UnitPresetUnit(string Blueprint, float X, float Y, float Z, float Heading);

/// <summary>
/// A group of a units preset, with its units and the groups in it.
/// </summary>
public sealed record UnitPresetGroup(string Name, IReadOnlyList<UnitPresetUnit> Units, IReadOnlyList<UnitPresetGroup> Groups);

/// <summary>
/// What a units preset holds (user stories U-4, F-10): a group of units to place again, for any
/// army, and how many units it has.
/// </summary>
public sealed record UnitPresetSettings(UnitPresetGroup Group, int Count);

/// <summary>
/// Units presets: the selected units, or the group chosen in the Units mode's tree, saved with
/// their places relative to their middle; applying one makes it what the place tool places, at
/// the mouse, for the tool's army (<c>units.placePreset</c>).
/// </summary>
public static class UnitPreset
{
    public const string Kind = "units";

    public static PresetKind Of(MainViewModel model) => new PresetKind(Kind, "Unit presets", () => Capture(model), preset => Apply(model, preset));

    /// <summary>
    /// The selected units (else the units of the group chosen in the tree) as a preset, named
    /// after their group.
    /// </summary>
    /// <exception cref="InvalidOperationException">No map, or no units to save.</exception>
    public static (string Name, JsonObject Settings) Capture(MainViewModel model)
    {
        MapDocument document = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
        string[] names = [.. model.Selection.Items.Where(thing => thing.Kind == ObjectKind.Unit).Select(thing => thing.Name!)];
        string name;
        if (names.Length > 0)
        {
            PlacedUnit first = document.FindUnit(names[0])!;
            name = first.Group.IsEmpty ? first.Army : first.Group[^1];
        }
        else if (model.Armies.Chosen is (string army, ImmutableArray<string> path))
        {
            names = [.. document.UnitsIn(army, path).Select(unit => unit.Unit.Name)];
            name = path.IsEmpty ? army : path[^1];
        }
        else
        {
            name = "";
        }
        if (names.Length == 0)
        {
            throw new InvalidOperationException("Select units, or choose a group with units in the Units mode's tree, to save them as a preset");
        }
        UnitGroup template = document.CaptureUnits(names, name);
        UnitPresetSettings settings = new UnitPresetSettings(ToPreset(template), names.Length);
        return ($"{name} ({names.Length})", Preset.Of(Kind, name, settings).Settings);
    }

    /// <summary>
    /// Makes the preset what the place tool places, and chooses the tool.
    /// </summary>
    public static void Apply(MainViewModel model, Preset preset)
    {
        UnitPresetSettings settings = preset.Read<UnitPresetSettings>();
        UnitPlaceTool tool = model.Tool<UnitPlaceTool>();
        model.SelectTool("units.place");
        tool.Preset = preset.Name;
        model.Status = $"Click to place the preset {preset.Name} ({settings.Count} units) for {tool.Army}; Q and E turn it";
    }

    /// <summary>
    /// The group of units a preset holds, to place with <see cref="UnitGroupEdits.PlaceUnits"/>.
    /// </summary>
    public static UnitGroup Template(Preset preset) => ToTemplate(preset.Read<UnitPresetSettings>().Group);

    private static UnitPresetGroup ToPreset(UnitGroup group) => new UnitPresetGroup(
        group.Name,
        [.. group.Units.Select(unit => new UnitPresetUnit(unit.Blueprint, unit.Position.X, unit.Position.Y, unit.Position.Z, unit.Orientation.Y))],
        [.. group.Groups.Select(ToPreset)]);

    private static UnitGroup ToTemplate(UnitPresetGroup group) => new UnitGroup
    {
        Name = group.Name,
        Units = [.. (group.Units ?? []).Select(unit => new Unit
        {
            Name = "",
            Blueprint = unit.Blueprint,
            Position = new Vector3(unit.X, unit.Y, unit.Z),
            Orientation = new Vector3(0, unit.Heading, 0),
        })],
        Groups = [.. (group.Groups ?? []).Select(ToTemplate)],
    };
}
