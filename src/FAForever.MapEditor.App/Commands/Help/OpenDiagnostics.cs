using FAForever.MapEditor.App.ViewModels;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Shows the choices about diagnostics: recording, sending, where to (user story F-17).
/// </summary>
[EditorCommand("help.diagnostics", "Diagnostics...", Story = "F-17")]
public sealed record OpenDiagnostics : IRequest;

public sealed class OpenDiagnosticsHandler(DiagnosticsViewModel diagnostics) : IRequestHandler<OpenDiagnostics>
{
    public Task Handle(OpenDiagnostics request, CancellationToken cancellationToken)
    {
        diagnostics.Open();
        return Task.CompletedTask;
    }
}
