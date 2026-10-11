using System.Collections.Immutable;
using System.Text.Json.Nodes;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Tools;

/// <summary>
/// Units presets: the selected units, or the group chosen in the Units mode's tree, saved with
/// their places relative to their middle; applying one makes it what the place tool places, at
/// the mouse, for the tool's army (<c>units.placePreset</c>).
/// </summary>
public static class UnitPreset
{
    public const string Kind = UnitPresets.Kind;

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
        if (names.Length > 0)
        {
            PlacedUnit first = document.FindUnit(names[0])!;
            return Capture(document, names, first.Group.IsEmpty ? first.Army : first.Group[^1]);
        }
        if (model.Armies.Chosen is (string army, ImmutableArray<string> path))
        {
            return CaptureGroup(model, army, path);
        }
        throw new InvalidOperationException("Select units, or choose a group with units in the Units mode's tree, to save them as a preset");
    }

    /// <summary>
    /// The units of a group and of the groups in it as a preset, named after the group, whatever is
    /// selected (the group's inspector's Save as a preset).
    /// </summary>
    /// <param name="path">Below the army's root group; empty for all the army's units.</param>
    /// <exception cref="InvalidOperationException">No map, or the group holds no units.</exception>
    /// <exception cref="ArgumentException">The map has no such army or group.</exception>
    public static (string Name, JsonObject Settings) CaptureGroup(MainViewModel model, string army, ImmutableArray<string> path)
    {
        MapDocument document = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
        if (document.FindGroup(army, path) is null)
        {
            throw new ArgumentException($"The army {army} has no group {UnitGroupEdits.PathText(path)}");
        }
        string[] names = [.. document.UnitsIn(army, path).Select(unit => unit.Unit.Name)];
        if (names.Length == 0)
        {
            throw new InvalidOperationException($"The group {(path.IsEmpty ? army : UnitGroupEdits.PathText(path))} holds no units to save as a preset");
        }
        return Capture(document, names, path.IsEmpty ? army : path[^1]);
    }

    private static (string Name, JsonObject Settings) Capture(MapDocument document, string[] names, string name)
    {
        UnitGroup template = document.CaptureUnits(names, name);
        UnitPresetSettings settings = new UnitPresetSettings(UnitPresets.ToPreset(template), names.Length);
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
        tool.UsePreset(preset.Name, UnitPresets.ToTemplate(settings.Group), settings.Role);
        string army = settings.Role is string role ? $"the {role} army ({ArmyText(model, role, tool.Army)})" : $"{tool.Army}";
        model.Status = $"Click to place the preset {preset.Name} ({settings.Count} units) for {army}; Q and E turn it";
    }

    /// <summary>
    /// The army a role's preset goes to on the open map, as the hint names it: for the player role
    /// the spawn nearest the mouse (else the middle), for an enemy on a map without an army for
    /// enemies the extra army placing it adds.
    /// </summary>
    internal static string ArmyText(MainViewModel model, string role, string fallback)
    {
        if (model.ShownDocument is not MapDocument document)
        {
            return fallback;
        }
        (float x, float z) = model.Pointer ?? (document.Width / 2f, document.Height / 2f);
        return UnitPresets.ArmyOrNewFor(document, role, x, z) switch
        {
            (string army, true) => $"{army}, a new extra army",
            (string army, false) => role == ArmyRoles.Player ? $"{army}, the nearest spawn's" : army,
            _ => fallback,
        };
    }

    /// <summary>
    /// The settings a preset holds.
    /// </summary>
    public static UnitPresetSettings Settings(Preset preset) => preset.Read<UnitPresetSettings>();
}
