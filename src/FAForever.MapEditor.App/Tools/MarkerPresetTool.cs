using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Tools;

/// <summary>
/// Places a markers preset of the workspace where one clicks (user story F-10), each click one
/// step of undo through <c>markers.placePreset</c>, which selects what it placed. The preset comes
/// from the Markers panel's presets (applying one chooses this tool). Escape selects nothing.
/// </summary>
public sealed partial class MarkerPresetTool(MainViewModel model) : ObservableObject, IMapTool
{
    /// <summary>
    /// The name of the markers preset a click places; null until one is applied.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    public partial string? Preset { get; set; }

    /// <summary>
    /// What a click places, as the options bar says it.
    /// </summary>
    public string Title => Preset is string name ? $"Preset {name}" : "No preset chosen";

    public double CursorRadius => 0;

    public string Help => "Click to place the preset there, around its middle; save one from selected markers in the panel's Marker presets";

    public void Press(float x, float z)
    {
        if (model.ShownDocument is null)
        {
            return;
        }
        if (Preset is null)
        {
            model.Status = "Choose a markers preset in the Markers panel first";
            return;
        }
        _ = model.RunWithAsync("markers.placePreset", new JsonObject { ["name"] = Preset, ["x"] = x, ["z"] = z });
    }

    public void Drag(float x, float z)
    {
    }

    public void Release()
    {
    }

    public bool Key(string key, ToolModifiers modifiers) => MarkerToolKeys.Escape(model, key);

    /// <summary>
    /// The markers preset kind: the selected markers saved, and a preset applied chooses this tool.
    /// </summary>
    public static PresetKind Kind(MainViewModel model) => new PresetKind(MarkerPresets.Kind, "Marker presets", () =>
    {
        MapDocument document = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
        int[] ids = [.. model.Selection.Items.Where(thing => thing.Kind == ObjectKind.Marker).Select(thing => thing.Id)];
        if (ids.Length == 0)
        {
            throw new InvalidOperationException("Select markers to save them as a preset");
        }
        MarkerPresetSettings settings = MarkerPresets.Capture(document, ids);
        string name = settings.Markers.Spawns.Count == 1 ? $"Spawn and {settings.Count - 1} markers" : $"Markers ({settings.Count})";
        return (name, Project.Workspaces.Preset.Of(MarkerPresets.Kind, name, settings).Settings);
    }, preset =>
    {
        MarkerPresetSettings settings = preset.Read<MarkerPresetSettings>();
        model.SelectTool("markers.preset");
        model.Tool<MarkerPresetTool>().Preset = preset.Name;
        model.Status = $"Click to place the preset {preset.Name} ({settings.Count} markers)";
    });
}
