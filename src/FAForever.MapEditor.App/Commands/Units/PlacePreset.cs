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
/// Places a units preset of the workspace (user story U-4), such as a civilian base, as a new
/// group of an army, one step of undo: its middle at a place (on a whole ogrid, so structures stay
/// on the build grid), turned in quarter turns, each unit as high above the terrain as it was.
/// What is left out is the place tool's: its army, group (the new group goes in it) and heading;
/// the place, the mouse's (else the middle of the map). A preset with an army role (the examples:
/// civilian, enemy, player) goes to the army of that role on this map (<see cref="UnitPresets.ArmyFor"/>)
/// unless the army is given (an enemy preset on a map with only the civilians' extra army adds
/// <c>ARMY_9</c> for it, in the same step, so the enemy is not the town's army); one of wrecks into
/// the army's <c>WRECKAGE</c> group. Selects the units
/// placed, with their images (a player's preset's images go to the partner spawns' armies).
/// </summary>
/// <param name="Name">The preset's name (Units mode, Presets); left out, the one the place tool places.</param>
/// <param name="X">In ogrids.</param>
/// <param name="Heading">In degrees, clockwise in the top view; rounded to quarter turns.</param>
/// <param name="Wreck">Whether to place them as wrecks; left out, as the preset says or the tool's As wrecks.</param>
/// <returns>The army, the new group's path and the units' names.</returns>
[UndoStep]
[EditorCommand("units.placePreset", "Place a preset", Story = "U-4")]
public sealed record PlaceUnitPreset(string? Name = null, float? X = null, float? Z = null, string? Army = null, string? Group = null, float? Heading = null, bool? Wreck = null) : IRequest<JsonObject>;

public sealed class PlaceUnitPresetHandler(MainViewModel model, PresetsViewModel presets, SelectionViewModel selection, CurrentEdit edit) : IRequestHandler<PlaceUnitPreset, JsonObject>
{
    public Task<JsonObject> Handle(PlaceUnitPreset request, CancellationToken cancellationToken)
    {
        PresetLibrary library = presets.Library ?? throw new InvalidOperationException("Presets need a workspace");
        UnitPlaceTool tool = model.Tool<UnitPlaceTool>();
        string name = request.Name ?? tool.Preset ?? throw new ArgumentException("Choose a preset in the Units mode's panel, or give its name as the argument name");
        Preset preset = library.Find(UnitPreset.Kind, name) ?? throw new ArgumentException($"There is no units preset '{name}'");
        UnitPresetSettings settings = UnitPreset.Settings(preset);
        MapDocument document = edit.Transaction.Document;
        ImmutableArray<string> parent = UnitGroupEdits.ParsePath(request.Group ?? tool.Group);
        (float x, float z) = (request.X, request.Z) is (float px, float pz) ? (px, pz) : model.Pointer ?? (document.Width / 2f, document.Height / 2f);
        x = MathF.Round(x);
        z = MathF.Round(z);
        float radians = BuildGrid.SnapHeading((float)((request.Heading ?? tool.Heading) * Math.PI / 180));
        bool? wreck = request.Wreck ?? (tool.AsWrecks ? true : null);

        ImmutableArray<string> names = [];
        ImmutableArray<string> group = [];
        ImmutableArray<ObjectRef> placed = [];
        string army = "";
        bool added = false;
        UnitCommands.Apply(edit, d =>
        {
            // the new group is named after the preset, not after the group its units were saved from
            int armies = d.Armies.Length;
            d = UnitPresets.Place(d, settings, preset.Name, request.Army, tool.Army, parent, wreck, x, z, radians, out names, out group, out army);
            added = d.Armies.Length > armies;
            // with the symmetry, the preset's units in every part of the map, in the same group (F-12)
            return model.MirrorPlaced(d, [.. names.Select(ObjectRef.Unit)], out placed);
        });
        // what was placed is selected, as placing a unit selects it
        selection.Set(placed);
        model.Status = $"Placed the preset {preset.Name}: {MainViewModel.Units(names.Length)} in {UnitGroupEdits.PathText(group)} of {army}"
            + (added ? $" (added the extra army {army} for the enemy role)" : "");
        return Task.FromResult(new JsonObject
        {
            ["army"] = army,
            ["addedArmy"] = added,
            ["group"] = UnitGroupEdits.PathText(group),
            ["units"] = new JsonArray([.. names.Select(name => (JsonNode)JsonValue.Create(name))]),
        });
    }
}
