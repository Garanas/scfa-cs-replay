using System.Collections.Immutable;
using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Saves the selected things of every kind but areas as one preset of the workspace (user story
/// F-10, kind <c>selection</c>): a civilian base with its units, props, decals and markers, around
/// their middle. The Map panel's Selection presets list them; <c>edit.placePreset</c> places one.
/// </summary>
/// <param name="Name">Left out: the group of the first unit, else <c>Selection (n)</c>; a preset with
/// this name is replaced.</param>
/// <returns>The preset's kind, name and what it holds.</returns>
[EditorCommand("edit.saveSelectionPreset", "Save the selection as a preset", Story = "F-10", Needs = Needs.Map | Needs.Selection)]
public sealed record SaveSelectionPreset(string? Name = null) : IRequest<JsonObject>;

public sealed class SaveSelectionPresetHandler(MainViewModel model, PresetsViewModel presets) : IRequestHandler<SaveSelectionPreset, JsonObject>
{
    public Task<JsonObject> Handle(SaveSelectionPreset request, CancellationToken cancellationToken)
    {
        Preset preset = presets.Save(SelectionPresets.Kind, request.Name);
        string summary = preset.Read<SelectionPresetSettings>().Summary();
        model.Status = $"Saved the preset {preset.Name}: {summary}";
        return Task.FromResult(new JsonObject { ["kind"] = preset.Kind, ["name"] = preset.Name, ["summary"] = summary });
    }
}

/// <summary>
/// Places a selection preset of the workspace (user story F-10), one step of undo: its things
/// around a place as they lay around their middle, turned about it, each as high above the terrain
/// as when saved; markers under free names with their links and chains, units as a new group named
/// after the preset in an army's <c>INITIAL</c>. A spawn of the preset moves the map's spawn nearest
/// to it there (its partners to the images), so the map keeps its armies, and the preset's units
/// are then that army's (a map without spawns gets a new army). With the symmetry, their images in
/// every part of the map (units of a playable army to the partner spawn's army). Selects
/// everything it placed and the spawns it moved.
/// </summary>
/// <param name="Name">The preset's name (Map panel, Selection presets); left out, the Preset tool's.</param>
/// <param name="X">In ogrids; left out, the mouse's place, else the middle of the map.</param>
/// <param name="Heading">In degrees, clockwise in the top view; left out, the Preset tool's.</param>
/// <param name="Army">The army of the preset's units; left out, the Preset tool's (the civilians).</param>
/// <returns>The references of what was placed, with its images.</returns>
[UndoStep]
[EditorCommand("edit.placePreset", "Place a selection preset", Story = "F-10", Needs = Needs.Map)]
public sealed record PlaceSelectionPreset(string? Name = null, float? X = null, float? Z = null, float? Heading = null, string? Army = null) : IRequest<JsonArray>;

public sealed class PlaceSelectionPresetHandler(MainViewModel model, PresetsViewModel presets, SelectionViewModel selection, CurrentEdit edit) : IRequestHandler<PlaceSelectionPreset, JsonArray>
{
    public Task<JsonArray> Handle(PlaceSelectionPreset request, CancellationToken cancellationToken)
    {
        PresetLibrary library = presets.Library ?? throw new InvalidOperationException("Presets need a workspace");
        SelectionPresetTool tool = model.Tool<SelectionPresetTool>();
        string name = request.Name ?? tool.Preset
            ?? throw new ArgumentException("Choose a selection preset in the Map panel, or give its name as the argument name");
        Preset preset = library.Find(SelectionPresets.Kind, name) ?? throw new ArgumentException($"There is no selection preset '{name}'");
        SelectionPresetSettings settings = preset.Read<SelectionPresetSettings>();
        MapDocument document = edit.Transaction.Document;
        tool.Refresh(document);
        (float x, float z) = (request.X, request.Z) is (float px, float pz) ? (px, pz) : model.Pointer ?? (document.Width / 2f, document.Height / 2f);
        // the middle on a whole ogrid, as it was saved: structures stay on the build grid
        x = MathF.Round(x);
        z = MathF.Round(z);
        float radians = (float)((request.Heading ?? tool.Heading) * Math.PI / 180);
        string? army = request.Army ?? tool.Army;
        ImmutableArray<ObjectRef> all = [];
        ImmutableArray<ObjectRef> moved = [];
        // a spawn of the preset moves one of the map's spawns (and its partners): the map keeps its armies
        IReadOnlyList<FAForever.FileFormats.Map.MapSymmetryTransform> parts = model.EditTransforms(document);
        edit.Transaction.Apply(d =>
        {
            d = SelectionPresets.Place(d, settings, preset.Name, army, [UnitGroupEdits.InitialGroup], x, z, radians, parts, out ImmutableArray<ObjectRef> added, out moved);
            // with the symmetry, the preset's things in every part of the map (F-12)
            return model.MirrorPlaced(d, added, out all);
        });
        all = [.. all, .. moved];
        selection.Set(all);
        model.Status = $"Placed the preset {preset.Name}: {settings.Summary()}" + (all.Length > settings.Count ? $", {all.Length} things with the symmetry's images" : "");
        return Task.FromResult(new JsonArray([.. all.Select(thing => (JsonNode)JsonValue.Create(thing.ToString()))]));
    }
}
