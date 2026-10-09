using FAForever.MapEditor.App.ViewModels;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Chooses a tool, in its own mode. Its commands, <c>tool.&lt;id&gt;</c>, are made from the tools of
/// <see cref="EditorModes"/>.
/// </summary>
public sealed record ChooseTool(string Id) : IRequest;

public sealed class ChooseToolHandler(MainViewModel model) : IRequestHandler<ChooseTool>
{
    public Task Handle(ChooseTool request, CancellationToken cancellationToken)
    {
        model.SelectTool(request.Id);
        return Task.CompletedTask;
    }
}
