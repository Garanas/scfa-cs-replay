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
/// the place, the mouse's (else the middle of the map).
/// </summary>
/// <param name="Name">The preset's name (Units mode, Presets); left out, the one the place tool places.</param>
/// <param name="X">In ogrids.</param>
/// <param name="Heading">In degrees, clockwise in the top view; rounded to quarter turns.</param>
/// <returns>The army, the new group's path and the units' names.</returns>
[UndoStep]
[EditorCommand("units.placePreset", "Place a preset", Story = "U-4")]
public sealed record PlaceUnitPreset(string? Name = null, float? X = null, float? Z = null, string? Army = null, string? Group = null, float? Heading = null) : IRequest<JsonObject>;

public sealed class PlaceUnitPresetHandler(MainViewModel model, PresetsViewModel presets, CurrentEdit edit) : IRequestHandler<PlaceUnitPreset, JsonObject>
{
    public Task<JsonObject> Handle(PlaceUnitPreset request, CancellationToken cancellationToken)
    {
        PresetLibrary library = presets.Library ?? throw new InvalidOperationException("Presets need a workspace");
        UnitPlaceTool tool = model.Tool<UnitPlaceTool>();
        string name = request.Name ?? tool.Preset ?? throw new ArgumentException("Choose a preset in the Units mode's panel, or give its name as the argument name");
        Preset preset = library.Find(UnitPreset.Kind, name) ?? throw new ArgumentException($"There is no units preset '{name}'");
        // the new group is named after the preset, not after the group its units were saved from
        UnitGroup template = UnitPreset.Template(preset) with { Name = preset.Name.Replace('/', '-') };
        MapDocument document = edit.Transaction.Document;
        string army = request.Army ?? tool.Army ?? throw new ArgumentException("The map has no army to place units for: add one in the Map mode");
        UnitCommands.RequireArmy(document, army);
        ImmutableArray<string> parent = UnitGroupEdits.ParsePath(request.Group ?? tool.Group);
        (float x, float z) = (request.X, request.Z) is (float px, float pz) ? (px, pz) : model.Pointer ?? (document.Width / 2f, document.Height / 2f);
        x = MathF.Round(x);
        z = MathF.Round(z);
        float radians = BuildGrid.SnapHeading((float)((request.Heading ?? tool.Heading) * Math.PI / 180));

        ImmutableArray<string> names = [];
        ImmutableArray<string> group = [];
        UnitCommands.Apply(edit, d =>
        {
            // the group to place into is made when missing, as placing a unit makes it
            if (d.FindGroup(army, parent) is null)
            {
                for (int depth = 1; depth <= parent.Length; depth++)
                {
                    if (d.FindGroup(army, parent[..depth]) is null)
                    {
                        d = d.AddGroup(army, parent[..(depth - 1)], parent[depth - 1], out _);
                    }
                }
            }
            d = d.PlaceUnits(template, army, parent, x, z, radians, out names, out group);
            // with the symmetry, the preset's units in every part of the map, in the same group (F-12)
            return model.MirrorPlaced(d, [.. names.Select(ObjectRef.Unit)], out _);
        });
        model.Status = $"Placed the preset {preset.Name}: {names.Length} units in {UnitGroupEdits.PathText(group)} of {army}";
        return Task.FromResult(new JsonObject
        {
            ["army"] = army,
            ["group"] = UnitGroupEdits.PathText(group),
            ["units"] = new JsonArray([.. names.Select(name => (JsonNode)JsonValue.Create(name))]),
        });
    }
}
