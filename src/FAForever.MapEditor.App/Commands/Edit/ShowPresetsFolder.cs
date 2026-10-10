using FAForever.MapEditor.App.ViewModels;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Shows the folder of a kind of preset in the system's file manager (user story F-10): the preset
/// sections' Show the folder. Its files are JSON, one per preset, that other tools may write too.
/// </summary>
/// <param name="Kind">E.g. <c>brushes</c>, <c>lighting</c>, <c>maps</c>; left out, the kind the panel lists.</param>
/// <returns>The folder.</returns>
[EditorCommand("presets.showFolder", "Show the folder of the presets", Story = "F-10")]
public sealed record ShowPresetKindFolder(string? Kind = null) : IRequest<string>;

public sealed class ShowPresetKindFolderHandler(MainViewModel model, PresetsViewModel presets) : IRequestHandler<ShowPresetKindFolder, string>
{
    public Task<string> Handle(ShowPresetKindFolder request, CancellationToken cancellationToken)
    {
        string folder = presets.FolderOf(request.Kind);
        model.ShowFolder(folder);
        return Task.FromResult(folder);
    }
}
