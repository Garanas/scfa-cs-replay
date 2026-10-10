using FAForever.MapEditor.App.ViewModels;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Opens a project; given a map folder of the game instead, imports it.
/// </summary>
/// <param name="Folder">Left out, the editor asks about unsaved changes and for the folder.</param>
/// <param name="Discard">Run without a person, whether the open map's unsaved changes may be lost; else it refuses.</param>
[EditorCommand("file.open", "Open project...", Gesture = "Ctrl+O", Story = "M-7", Needs = Needs.Idle)]
public sealed record OpenProject(string? Folder = null, bool? Discard = null) : IRequest;

public sealed class OpenProjectHandler(MainViewModel model, IEditorDialogs dialogs) : IRequestHandler<OpenProject>
{
    public async Task Handle(OpenProject request, CancellationToken cancellationToken)
    {
        if (request.Folder is string folder)
        {
            UnsavedChanges.RefuseToDiscard(model, dialogs, request.Discard, "open another map");
            await model.OpenAsync(folder);
            return;
        }
        if (await model.ConfirmCloseAsync() && await dialogs.PickFolderAsync("Open a project", model.Setup.Workspace?.ProjectsFolder) is string picked)
        {
            await model.LoadProjectAsync(picked);
        }
    }
}
