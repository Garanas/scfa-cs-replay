using FAForever.MapEditor.App.ViewModels;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Generates the map's preview page into a folder (user story M-11): <c>index.html</c>, one page
/// without scripts about the map, and its pictures in <c>assets</c>, made from the map as it is with
/// the workspace's textures and blueprints (<see cref="Project.Generation.PreviewPageFiles"/>). The
/// map itself does not change.
/// </summary>
/// <param name="Folder">Where the page goes (made when missing); left out, the editor asks, starting in the folder used last.</param>
/// <returns>The page's file; null when no folder was chosen.</returns>
[EditorCommand("generate.previewPage", "Preview page", Story = "M-11", Needs = Needs.Map | Needs.Idle)]
public sealed record GeneratePreviewPage(string? Folder = null) : IRequest<string?>;

public sealed class GeneratePreviewPageHandler(MainViewModel model, IEditorDialogs dialogs) : IRequestHandler<GeneratePreviewPage, string?>
{
    public async Task<string?> Handle(GeneratePreviewPage request, CancellationToken cancellationToken)
    {
        string? folder = request.Folder ?? await dialogs.PickFolderAsync("Generate the preview page into a folder", model.PreviewPageFolder);
        return folder is null ? null : await model.GeneratePreviewPageAsync(folder);
    }
}
