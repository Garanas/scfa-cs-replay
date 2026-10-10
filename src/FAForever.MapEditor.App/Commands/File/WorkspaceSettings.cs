using FAForever.MapEditor.App.ViewModels;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// The workspace's settings (user story F-5): without arguments it opens their page (the game's
/// folder, FAF's data folder, the export folder, the converted assets); with folders it writes them
/// into <c>workspace.json</c>, keeping the ones left out.
/// </summary>
/// <param name="GameFolder">The folder the game is installed in, with <c>gamedata/env.scd</c>.</param>
/// <param name="FafDataFolder">FAF's <c>gamedata</c> folder; empty for none.</param>
/// <param name="VaultFolder">The vault; maps are exported into its <c>maps</c>.</param>
/// <returns>What the page shows, as <c>state.workspaceSettings</c>.</returns>
[EditorCommand("file.workspaceSettings", "Workspace settings", Story = "F-5", Needs = Needs.Workspace)]
public sealed record WorkspaceSettings(string? GameFolder = null, string? FafDataFolder = null, string? VaultFolder = null) : IRequest<System.Text.Json.Nodes.JsonObject>;

public sealed class WorkspaceSettingsHandler(MainViewModel model) : IRequestHandler<WorkspaceSettings, System.Text.Json.Nodes.JsonObject>
{
    public async Task<System.Text.Json.Nodes.JsonObject> Handle(WorkspaceSettings request, CancellationToken cancellationToken)
    {
        WorkspaceSettingsViewModel page = model.WorkspaceSettings;
        if (request.GameFolder is null && request.FafDataFolder is null && request.VaultFolder is null)
        {
            await page.OpenAsync();
            return page.Describe();
        }
        if (!await page.ApplyAsync(request.GameFolder, request.FafDataFolder, request.VaultFolder))
        {
            throw new ArgumentException(page.Error);
        }
        model.Status = page.AssetsCurrent ? "Saved the workspace's settings"
            : "Saved the workspace's settings; the assets are to be converted again (File, Workspace settings, Convert again)";
        return page.Describe();
    }
}
