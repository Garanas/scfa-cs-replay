using System.Collections.Immutable;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Tools;

/// <summary>
/// Places a selection preset of the workspace where one clicks (user story F-10): things of
/// several kinds at once, such as a civilian base with its units, props, decals and markers. Each
/// click is one step of undo through <c>edit.placePreset</c>, which mirrors what it placed with the
/// symmetry and selects it all. The preset comes from the Map panel's Selection presets (applying
/// one chooses this tool). Q and E turn what is placed next by a quarter turn (structures stay on
/// the build grid), with shift by 15 degrees; Escape selects nothing.
/// </summary>
public sealed partial class SelectionPresetTool(MainViewModel model) : ObservableObject, IMapTool
{
    /// <summary>
    /// The name of the selection preset a click places; null until one is applied.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    public partial string? Preset { get; set; }

    /// <summary>
    /// What the preset holds, in words (<see cref="SelectionPresetSettings.Summary"/>).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    public partial string Summary { get; set; } = "";

    /// <summary>
    /// What a click places, as the options bar says it.
    /// </summary>
    public string Title => Preset is string name ? $"Preset {name} ({Summary})" : "No preset chosen";

    /// <summary>
    /// The armies of the map, for the combo box.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<string> Armies { get; private set; } = [];

    /// <summary>
    /// The army the preset's units are placed for: the civilians unless another is chosen. They go
    /// into a new group named after the preset, in the army's <c>INITIAL</c>.
    /// </summary>
    [ObservableProperty]
    public partial string? Army { get; set; }

    /// <summary>
    /// Which way what it places next is turned, in degrees clockwise in the top view.
    /// </summary>
    [ObservableProperty]
    public partial double Heading { get; set; }

    public double CursorRadius => 0;

    public string Help => "Click to place the preset there, around its middle; Q and E turn it; save one from a selection in the Map panel's Selection presets";

    /// <summary>
    /// Its settings for automation's <c>state</c>.
    /// </summary>
    public JsonObject Describe() => new JsonObject
    {
        ["preset"] = Preset,
        ["summary"] = Summary,
        ["army"] = Army,
        ["heading"] = Heading,
    };

    public void Press(float x, float z)
    {
        if (model.ShownDocument is null)
        {
            return;
        }
        if (Preset is null)
        {
            model.Status = "Choose a selection preset in the Map panel's Selection presets first";
            return;
        }
        _ = model.RunWithAsync("edit.placePreset", new JsonObject { ["name"] = Preset, ["x"] = x, ["z"] = z });
    }

    public void Drag(float x, float z)
    {
    }

    public void Release()
    {
    }

    public bool Key(string key, ToolModifiers modifiers)
    {
        if (key == "Escape")
        {
            model.Selection.Clear();
            return true;
        }
        if (modifiers.HasFlag(ToolModifiers.Control) || modifiers.HasFlag(ToolModifiers.Alt) || key is not ("Q" or "E"))
        {
            return false;
        }
        double step = modifiers.HasFlag(ToolModifiers.Shift) ? 15 : 90;
        double heading = (Heading + (key == "E" ? step : -step)) % 360;
        Heading = heading < 0 ? heading + 360 : heading;
        model.Status = $"Places the preset turned {Heading:0}°";
        return true;
    }

    /// <summary>
    /// Shows the armies of a version of the document; an army it no longer has makes way for the
    /// civilians.
    /// </summary>
    public void Refresh(MapDocument? document)
    {
        ImmutableArray<Army> armies = document?.Armies ?? [];
        if (!Armies.SequenceEqual(armies.Select(army => army.Name)))
        {
            Armies = [.. armies.Select(army => army.Name)];
        }
        if (document is not null && (Army is null || document.FindArmy(Army) is null))
        {
            Army = AdaptiveGroups.CivilianArmy(document) ?? armies.FirstOrDefault()?.Name;
        }
    }

    /// <summary>
    /// The selection preset kind: the selected things saved (every kind but areas), and a preset
    /// applied chooses this tool in the Map mode.
    /// </summary>
    public static PresetKind Kind(MainViewModel model) => new PresetKind(SelectionPresets.Kind, "Selection presets", () =>
    {
        MapDocument document = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
        ObjectRef[] things = [.. model.Selection.Items.Where(thing => thing.Kind != ObjectKind.Area)];
        if (things.Length == 0)
        {
            throw new InvalidOperationException("Select things to save them as a preset: units, props, decals and markers (areas are left out)");
        }
        string name = SuggestedName(document, things);
        SelectionPresetSettings settings = SelectionPresets.Capture(document, things, name);
        return (name, Project.Workspaces.Preset.Of(SelectionPresets.Kind, name, settings).Settings);
    }, preset =>
    {
        SelectionPresetSettings settings = preset.Read<SelectionPresetSettings>();
        model.SelectTool("map.preset");
        SelectionPresetTool tool = model.Tool<SelectionPresetTool>();
        tool.Summary = settings.Summary();
        tool.Preset = preset.Name;
        model.Status = $"Click to place the preset {preset.Name} ({tool.Summary}); Q and E turn it";
    });

    /// <summary>
    /// A name for the selection: the group of its first unit (a base), else what it holds.
    /// </summary>
    private static string SuggestedName(MapDocument document, IReadOnlyList<ObjectRef> things)
    {
        if (things.FirstOrDefault(thing => thing.Kind == ObjectKind.Unit) is { Name: string unit } && document.FindUnit(unit) is PlacedUnit placed && !placed.Group.IsEmpty
            && placed.Group[^1] != UnitGroupEdits.InitialGroup)
        {
            return placed.Group[^1];
        }
        return $"Selection ({things.Count})";
    }
}
