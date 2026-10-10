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
