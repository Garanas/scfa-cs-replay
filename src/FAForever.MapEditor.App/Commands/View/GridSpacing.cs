using FAForever.MapEditor.App.ViewModels;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Sets the ogrids between the grid's lines and shows the grid (user story V-3); the Grid chip
/// says the spacing, and its context menu offers the usual ones.
/// </summary>
/// <param name="Spacing">Ogrids between the lines, 1 to 256.</param>
[EditorCommand("view.gridSpacing", "Grid spacing...", Story = "V-3")]
public sealed record SetGridSpacing(int Spacing = 16) : IRequest;

public sealed class SetGridSpacingHandler(MainViewModel model) : IRequestHandler<SetGridSpacing>
{
    public Task Handle(SetGridSpacing request, CancellationToken cancellationToken)
    {
        model.SetGridSpacing(request.Spacing);
        model.Status = $"The grid's lines lie {request.Spacing} {(request.Spacing == 1 ? "ogrid" : "ogrids")} apart";
        return Task.CompletedTask;
    }
}
