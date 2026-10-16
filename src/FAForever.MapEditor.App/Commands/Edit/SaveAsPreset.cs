using System.Text.Json.Nodes;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Workspaces;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Saves settings as a preset in the workspace (user story F-10): today the active brush tool's
/// (kind <c>brush</c>: the tool, radius, strength, mode, shape, rotation; user story T-2). Other
/// kinds come with their features (<see cref="PresetsViewModel.Register"/>).
/// </summary>
/// <param name="Kind">Left out: the kind the mode's panel lists (<c>brush</c> in the Terrain and Paint modes).</param>
/// <param name="Name">Left out: a name made from the settings, such as <c>Raise 12</c>; a preset of the
/// kind with this name is replaced.</param>
/// <returns>The preset's kind and name.</returns>
[EditorCommand("edit.saveAsPreset", "Save as a preset", Story = "F-10")]
public sealed record SaveAsPreset(string? Kind = null, string? Name = null) : IRequest<JsonObject>;

public sealed class SaveAsPresetHandler(MainViewModel model, PresetsViewModel presets) : IRequestHandler<SaveAsPreset, JsonObject>
{
    public Task<JsonObject> Handle(SaveAsPreset request, CancellationToken cancellationToken)
    {
        Preset preset = presets.Save(request.Kind, request.Name);
        model.Status = $"Saved the preset {preset.Name} ({preset.Kind})";
        return Task.FromResult(new JsonObject { ["kind"] = preset.Kind, ["name"] = preset.Name });
    }
}
