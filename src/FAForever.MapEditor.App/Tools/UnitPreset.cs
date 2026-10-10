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
        string army = settings.Role is string role
            ? $"the {role} army ({(model.ShownDocument is MapDocument document ? UnitPresets.ArmyFor(document, role, document.Width / 2f, document.Height / 2f) ?? tool.Army : tool.Army)})"
            : $"{tool.Army}";
        model.Status = $"Click to place the preset {preset.Name} ({settings.Count} units) for {army}; Q and E turn it";
    }

    /// <summary>
    /// The settings a preset holds.
    /// </summary>
    public static UnitPresetSettings Settings(Preset preset) => preset.Read<UnitPresetSettings>();
}
