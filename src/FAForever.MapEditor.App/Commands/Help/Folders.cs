using FAForever.MapEditor.App.ViewModels;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Shows the workspace's folder (its projects, assets and settings) in the system's file manager
/// (user story F-5).
/// </summary>
[EditorCommand("help.workspaceFolder", "Show the workspace folder", Story = "F-5", Needs = Needs.Workspace)]
public sealed record ShowWorkspaceFolder : IRequest;

public sealed class ShowWorkspaceFolderHandler(MainViewModel model) : IRequestHandler<ShowWorkspaceFolder>
{
    public Task Handle(ShowWorkspaceFolder request, CancellationToken cancellationToken)
    {
        model.ShowWorkspaceFolder();
        return Task.CompletedTask;
    }
}

/// <summary>
/// Shows the folder with the sessions' logs and metrics (user story F-17).
/// </summary>
[EditorCommand("help.diagnosticsFolder", "Show the diagnostics folder", Story = "F-17")]
public sealed record ShowDiagnosticsFolder : IRequest;

public sealed class ShowDiagnosticsFolderHandler(MainViewModel model) : IRequestHandler<ShowDiagnosticsFolder>
{
    public Task Handle(ShowDiagnosticsFolder request, CancellationToken cancellationToken)
    {
        model.ShowDiagnosticsFolder();
        return Task.CompletedTask;
    }
}
