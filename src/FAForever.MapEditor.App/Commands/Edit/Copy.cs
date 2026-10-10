using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Copies the selected things (user story F-11), to paste them with <c>edit.paste</c>.
/// </summary>
/// <returns>How many things were copied.</returns>
[EditorCommand("edit.copy", "Copy", Gesture = "Ctrl+C", Story = "F-11", Needs = Needs.Map | Needs.Selection)]
public sealed record Copy : IRequest<int>;

public sealed class CopyHandler(MainViewModel model, SelectionViewModel selection) : IRequestHandler<Copy, int>
{
    public Task<int> Handle(Copy request, CancellationToken cancellationToken)
    {
        MapDocument document = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
        selection.Clipboard = document.CopyObjects(selection.Items);
        model.Status = $"Copied {selection.Summary}";
        return Task.FromResult(selection.Clipboard.Items.Length);
    }
}
