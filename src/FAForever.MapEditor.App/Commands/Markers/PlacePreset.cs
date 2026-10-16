using System.Collections.Immutable;
using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Workspaces;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Places a markers preset of the workspace (user story F-10), such as a spawn with its deposits,
/// one step of undo: its markers around a place as they lay around their middle, each as high above
/// the terrain as when saved, under free names (a spawn becomes the next playable army), path nodes
/// linked among each other and chains made again. Selects what it placed. Not mirrored by the
/// symmetry.
/// </summary>
/// <param name="Name">The preset's name (Markers panel, Marker presets); left out, the preset tool's.</param>
/// <param name="X">In ogrids; left out, the mouse's place, else the middle of the map.</param>
/// <returns>The references of the markers placed.</returns>
[UndoStep]
[EditorCommand("markers.placePreset", "Place a markers preset", Story = "F-10", Needs = Needs.Map)]
public sealed record PlaceMarkerPreset(string? Name = null, float? X = null, float? Z = null) : IRequest<JsonArray>;

public sealed class PlaceMarkerPresetHandler(MainViewModel model, PresetsViewModel presets, SelectionViewModel selection, CurrentEdit edit) : IRequestHandler<PlaceMarkerPreset, JsonArray>
{
    public Task<JsonArray> Handle(PlaceMarkerPreset request, CancellationToken cancellationToken)
    {
        PresetLibrary library = presets.Library ?? throw new InvalidOperationException("Presets need a workspace");
        string name = request.Name ?? model.Tool<MarkerPresetTool>().Preset
            ?? throw new ArgumentException("Choose a markers preset in the Markers panel, or give its name as the argument name");
        Preset preset = library.Find(MarkerPresets.Kind, name) ?? throw new ArgumentException($"There is no markers preset '{name}'");
        MarkerPresetSettings settings = preset.Read<MarkerPresetSettings>();
        MapDocument document = edit.Transaction.Document;
        (float x, float z) = (request.X, request.Z) is (float px, float pz) ? (px, pz) : model.Pointer ?? (document.Width / 2f, document.Height / 2f);
        ImmutableArray<ObjectRef> added = [];
        edit.Transaction.Apply(d => MarkerPresets.Place(d, settings, x, z, out added));
        selection.Set(added);
        model.Status = $"Placed the preset {preset.Name}: {added.Length} markers";
        return Task.FromResult(new JsonArray([.. added.Select(thing => (JsonNode)JsonValue.Create(thing.ToString()))]));
    }
}
