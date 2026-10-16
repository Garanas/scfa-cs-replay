using FAForever.MapEditor.App.ViewModels;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Sets up the workspace as the setup page holds it, remembers it and converts the game's assets
/// into it (user story F-5). The fields given replace the page's; the others keep what it suggests.
/// </summary>
/// <param name="Folder">Where the workspace goes; a folder that holds one already is used as it is.</param>
/// <param name="Game">The game's installation.</param>
/// <param name="Faf">FAF's <c>gamedata</c> folder; empty without FAF.</param>
/// <param name="Vault">The vault on disk, where maps are exported to.</param>
/// <returns>Whether it is set up; the page's error says why not.</returns>
[EditorCommand("workspace.setUp", "Set up the workspace", Story = "F-5")]
public sealed record SetUpWorkspace(string? Folder = null, string? Game = null, string? Faf = null, string? Vault = null) : IRequest<bool>;

public sealed class SetUpWorkspaceHandler(WorkspaceSetupViewModel setup) : IRequestHandler<SetUpWorkspace, bool>
{
    public Task<bool> Handle(SetUpWorkspace request, CancellationToken cancellationToken)
    {
        setup.Folder = request.Folder ?? setup.Folder;
        setup.GameFolder = request.Game ?? setup.GameFolder;
        setup.FafDataFolder = request.Faf ?? setup.FafDataFolder;
        setup.VaultFolder = request.Vault ?? setup.VaultFolder;
        return setup.SetUpAsync();
    }
}
