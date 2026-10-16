using FAForever.MapEditor.App.ViewModels;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Saves the project; a map that is no project yet is saved as one.
/// </summary>
/// <param name="Folder">Where a map that is no project yet goes; left out, the editor asks.</param>
[EditorCommand("file.save", "Save", Gesture = "Ctrl+S", Story = "M-7", Needs = Needs.Map | Needs.Idle)]
public sealed record Save(string? Folder = null) : IRequest;

public sealed class SaveHandler(MainViewModel model) : IRequestHandler<Save>
{
    public Task Handle(Save request, CancellationToken cancellationToken) => model.SaveProjectAsync(request.Folder);
}
