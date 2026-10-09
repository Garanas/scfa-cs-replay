using FAForever.MapEditor.App.ViewModels;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Exports the map as a map folder of the game.
/// </summary>
/// <param name="Folder">Where the map folder goes; left out, the workspace's vault, else the editor asks.</param>
/// <param name="Version">The map's version, which its folder name carries.</param>
/// <returns>The map folder that was written; null when nothing was chosen.</returns>
[EditorCommand("file.export", "Export to the vault", Gesture = "Ctrl+E", Story = "M-7", Needs = Needs.Map | Needs.Idle)]
public sealed record Export(string? Folder = null, int? Version = null) : IRequest<string?>;

public sealed class ExportHandler(MainViewModel model, IEditorDialogs dialogs) : IRequestHandler<Export, string?>
{
    public async Task<string?> Handle(Export request, CancellationToken cancellationToken)
    {
        string? folder = request.Folder ?? model.Workspace?.MapsFolder ?? await dialogs.PickFolderAsync("Export into a folder, e.g. the game's maps folder");
        return folder is null ? null : await model.ExportAsync(folder, request.Version);
    }
}
