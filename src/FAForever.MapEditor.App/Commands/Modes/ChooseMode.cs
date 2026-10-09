using FAForever.MapEditor.App.ViewModels;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Chooses a mode on the rail, and the tool last used in it. Its commands, <c>&lt;mode&gt;.mode</c>,
/// are made from <see cref="EditorModes"/>.
/// </summary>
public sealed record ChooseMode(string Id) : IRequest;

public sealed class ChooseModeHandler(MainViewModel model) : IRequestHandler<ChooseMode>
{
    public Task Handle(ChooseMode request, CancellationToken cancellationToken)
    {
        model.SelectMode(request.Id);
        return Task.CompletedTask;
    }
}
