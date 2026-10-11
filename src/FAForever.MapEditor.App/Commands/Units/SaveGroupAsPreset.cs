using System.Collections.Immutable;
using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Workspaces;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Saves the units of a group (and of the groups in it) as a units preset (user story U-4),
/// whatever is selected: the group's inspector's Save as a preset. Left out, the army and group
/// chosen in the Units mode's tree.
/// </summary>
/// <param name="Group">Its path (<c>INITIAL/Town</c>); empty, with an army given, for all the army's units.</param>
/// <param name="Name">Left out: the group's name with its count of units, made unique.</param>
/// <returns>The preset's name and how many units it holds.</returns>
[EditorCommand("units.saveGroupAsPreset", "Save a group as a preset", Story = "U-4", Needs = Needs.Map)]
public sealed record SaveUnitGroupAsPreset(string? Army = null, string? Group = null, string? Name = null) : IRequest<JsonObject>;

public sealed class SaveUnitGroupAsPresetHandler(MainViewModel model, ArmiesViewModel armies, PresetsViewModel presets) : IRequestHandler<SaveUnitGroupAsPreset, JsonObject>
{
    public Task<JsonObject> Handle(SaveUnitGroupAsPreset request, CancellationToken cancellationToken)
    {
        string army = request.Army ?? armies.Chosen?.Army
            ?? throw new ArgumentException("Choose an army or group in the Units mode's tree, or give the argument army");
        ImmutableArray<string> path = request.Army is not null ? UnitGroupEdits.ParsePath(request.Group) : armies.Chosen!.Value.Path;
        (string suggested, JsonObject settings) = UnitPreset.CaptureGroup(model, army, path);
        Preset preset = presets.Save(UnitPreset.Kind, request.Name, (suggested, settings));
        int count = preset.Read<UnitPresetSettings>().Count;
        model.Status = $"Saved the preset {preset.Name} ({MainViewModel.Units(count)} of {(path.IsEmpty ? army : $"{UnitGroupEdits.PathText(path)} of {army}")})";
        return Task.FromResult(new JsonObject { ["kind"] = preset.Kind, ["name"] = preset.Name, ["units"] = count });
    }
}
