using FAForever.MapEditor.App.ViewModels;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Chooses the mask for tools that the masks tool paints and the overlay shows (user story K-1),
/// as the panel's list does.
/// </summary>
[EditorCommand("paint.chooseMask", "Choose a mask", Story = "K-1", Needs = Needs.Map)]
public sealed record ChooseMask(string Name) : IRequest;

public sealed class ChooseMaskHandler(MainViewModel model) : IRequestHandler<ChooseMask>
{
    public Task Handle(ChooseMask request, CancellationToken cancellationToken)
    {
        model.Masks.Choose(request.Name);
        return Task.CompletedTask;
    }
}
