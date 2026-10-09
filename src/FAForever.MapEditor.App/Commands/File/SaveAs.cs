using FAForever.MapEditor.App.ViewModels;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Saves the map as a project in an empty folder, which it is from then on.
/// </summary>
/// <param name="Folder">Left out, the editor asks for it.</param>
[EditorCommand("file.saveAs", "Save as project...", Gesture = "Ctrl+Shift+S", Story = "M-7", Needs = Needs.Map | Needs.Idle)]
public sealed record SaveAs(string? Folder = null) : IRequest;

public sealed class SaveAsHandler(MainViewModel model, IEditorDialogs dialogs) : IRequestHandler<SaveAs>
{
    public async Task Handle(SaveAs request, CancellationToken cancellationToken)
    {
        if ((request.Folder ?? await dialogs.PickFolderAsync("Save as a project in an empty folder", model.Setup.Workspace?.ProjectsFolder)) is string folder)
        {
            await model.SaveAsAsync(folder);
        }
    }
}
