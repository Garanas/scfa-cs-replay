using FAForever.MapEditor.App.ViewModels;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Exports the map as a map folder of the game (user stories M-7, M-8). A preview out of date is
/// rendered first; the status line names the version written and the next one.
/// </summary>
/// <param name="Folder">Where the map folder goes; left out, the workspace's vault, else the editor asks.</param>
/// <param name="Version">The version to write, which its folder name carries, without changing the map's own.</param>
/// <param name="Raise">Raise the map's own version by one first (a step of undo), so the export is
/// a new version of the map in a new folder (<c>name.v0004</c>) and the scenario's <c>map_version</c>.</param>
/// <returns>The map folder that was written; null when nothing was chosen.</returns>
[EditorCommand("file.export", "Export to the vault", Gesture = "Ctrl+E", Story = "M-7", Needs = Needs.Map | Needs.Idle)]
public sealed record Export(string? Folder = null, int? Version = null, bool Raise = false) : IRequest<string?>;

public sealed class ExportHandler(MainViewModel model, IEditorDialogs dialogs) : IRequestHandler<Export, string?>
{
    public async Task<string?> Handle(Export request, CancellationToken cancellationToken)
    {
        string? folder = request.Folder ?? model.Setup.Workspace?.MapsFolder ?? await dialogs.PickFolderAsync("Export into a folder, e.g. the game's maps folder");
        return folder is null ? null : await model.ExportAsync(folder, request.Version, request.Raise);
    }
}
