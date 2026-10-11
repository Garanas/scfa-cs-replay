using System.Text.Json.Nodes;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Workspaces;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Applies a preset of the workspace (user story F-10): a brush preset chooses its tool and gives
/// it the preset's settings. Its control is the preset's button in the mode's panel.
/// </summary>
/// <param name="Kind">E.g. <c>brush</c>.</param>
[EditorCommand("presets.apply", "Apply a preset", Story = "F-10")]
public sealed record ApplyPreset(string Kind, string Name) : IRequest;

public sealed class ApplyPresetHandler(MainViewModel model, PresetsViewModel presets) : IRequestHandler<ApplyPreset>
{
    public Task Handle(ApplyPreset request, CancellationToken cancellationToken)
    {
        string before = model.Status;
        Preset preset = presets.Apply(request.Kind, request.Name);
        if (model.Status == before)
        {
            model.Status = $"Applied the preset {preset.Name} ({preset.Kind})";
        }
        return Task.CompletedTask;
    }
}

/// <summary>
/// Deletes a preset of the workspace (user story F-10). Its control is the preset's delete button
/// in the mode's panel.
/// </summary>
[EditorCommand("presets.delete", "Delete a preset", Story = "F-10")]
public sealed record DeletePreset(string Kind, string Name) : IRequest;

public sealed class DeletePresetHandler(MainViewModel model, PresetsViewModel presets) : IRequestHandler<DeletePreset>
{
    public Task Handle(DeletePreset request, CancellationToken cancellationToken)
    {
        presets.Delete(request.Kind, request.Name);
        model.Status = $"Deleted the preset {request.Name} ({request.Kind})";
        return Task.CompletedTask;
    }
}

/// <summary>
/// Narrows the Unit presets list (user story U-9): <c>All</c>, <c>Starred</c>, <c>Civilian</c>,
/// <c>Wrecks</c> or <c>Mine</c> (the presets the user saved, not the examples the editor ships). Its
/// controls are the chips above the list.
/// </summary>
/// <param name="Filter">One of the names above, ignoring case.</param>
/// <returns>The filter and the names of the presets it leaves.</returns>
[EditorCommand("presets.filter", "Filter the unit presets", Story = "U-9")]
public sealed record FilterUnitPresets(string Filter) : IRequest<JsonObject>;

public sealed class FilterUnitPresetsHandler(MainViewModel model, PresetsViewModel presets) : IRequestHandler<FilterUnitPresets, JsonObject>
{
    public async Task<JsonObject> Handle(FilterUnitPresets request, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse(request.Filter, ignoreCase: true, out PresetFilter filter) || !Enum.IsDefined(filter))
        {
            throw new ArgumentException($"'{request.Filter}' is no filter: {string.Join(", ", Enum.GetNames<PresetFilter>())}");
        }
        await presets.ChooseFilter(filter);
        model.Status = filter == PresetFilter.All ? $"Showing all {presets.Shown.Count} unit presets" : $"Showing the {filter} unit presets: {presets.Shown.Count}";
        return new JsonObject
        {
            ["filter"] = filter.ToString(),
            ["shown"] = new JsonArray([.. presets.Shown.Select(preset => (JsonNode)JsonValue.Create(preset.Name)!)]),
        };
    }
}

/// <summary>
/// Stars a preset or takes its star away (user story U-9), as the shelf stars assets: the star is
/// kept in the workspace's <c>favourites.json</c>, so it shows in every map. <c>on</c> left out turns
/// it around. Its control is the star on the preset's row.
/// </summary>
/// <param name="Kind">E.g. <c>units</c>.</param>
/// <returns>The preset's name and whether it is starred now.</returns>
[EditorCommand("presets.star", "Star a preset", Story = "U-9")]
public sealed record StarPreset(string Kind, string Name, bool? On = null) : IRequest<JsonObject>;

public sealed class StarPresetHandler(MainViewModel model, PresetsViewModel presets) : IRequestHandler<StarPreset, JsonObject>
{
    public Task<JsonObject> Handle(StarPreset request, CancellationToken cancellationToken)
    {
        Preset preset = presets.Library?.Find(request.Kind, request.Name) ?? throw new ArgumentException($"The workspace has no {request.Kind} preset '{request.Name}'");
        bool starred = presets.SetStar(preset.Kind, preset.Name, request.On ?? !presets.IsStarred(preset));
        model.Status = starred ? $"Starred the preset {preset.Name}: the Starred filter shows it" : $"Took the star of the preset {preset.Name} away";
        return Task.FromResult(new JsonObject { ["name"] = preset.Name, ["starred"] = starred });
    }
}
