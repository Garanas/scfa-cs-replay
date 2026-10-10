using FAForever.MapEditor.App.ViewModels;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Imports a map folder of the game; it becomes a project when it is saved as one.
/// </summary>
/// <param name="Folder">Left out, the editor asks about unsaved changes and for the folder.</param>
/// <param name="Discard">Run without a person, whether the open map's unsaved changes may be lost; else it refuses.</param>
[EditorCommand("file.import", "Import map folder...", Gesture = "Ctrl+I", Story = "M-7", Needs = Needs.Idle)]
public sealed record ImportMap(string? Folder = null, bool? Discard = null) : IRequest;

public sealed class ImportMapHandler(MainViewModel model, IEditorDialogs dialogs) : IRequestHandler<ImportMap>
{
    public async Task Handle(ImportMap request, CancellationToken cancellationToken)
    {
        if (request.Folder is string folder)
        {
            UnsavedChanges.RefuseToDiscard(model, dialogs, request.Discard, "import a map");
            await model.ImportMapFolderAsync(folder);
            return;
        }
        if (await model.ConfirmCloseAsync() && await dialogs.PickFolderAsync("Import a map folder of the game", model.Setup.Workspace?.MapsFolder) is string picked)
        {
            await model.ImportMapFolderAsync(picked);
        }
    }
}
