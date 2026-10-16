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
        string status = model.Status;
        model.SelectTool(request.Id);
        // the status line says the tool, as choosing a mode does; unless choosing it said something of its own
        if (model.Status == status)
        {
            model.Status = $"{model.SelectedMode!.Definition.Title} mode: {model.ActiveTool?.Definition.Title ?? "no tool"}";
        }
        return Task.CompletedTask;
    }
}
