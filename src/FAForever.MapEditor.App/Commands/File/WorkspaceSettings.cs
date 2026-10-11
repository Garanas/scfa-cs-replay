using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Workspaces;
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
/// <param name="AutoGenerate">The Procedural defaults: the generated results new and imported maps regenerate on their own (ids).</param>
/// <param name="OnExport">The Procedural defaults: the results their export makes or writes (<c>normalMap</c>, <c>pathNetworks</c>).</param>
/// <returns>What the page shows, as <c>state.workspaceSettings</c>.</returns>
[EditorCommand("file.workspaceSettings", "Workspace settings", Story = "F-5", Needs = Needs.Workspace)]
public sealed record WorkspaceSettings(string? GameFolder = null, string? FafDataFolder = null, string? VaultFolder = null,
    string[]? AutoGenerate = null, string[]? OnExport = null) : IRequest<System.Text.Json.Nodes.JsonObject>;

public sealed class WorkspaceSettingsHandler(MainViewModel model) : IRequestHandler<WorkspaceSettings, System.Text.Json.Nodes.JsonObject>
{
    public async Task<System.Text.Json.Nodes.JsonObject> Handle(WorkspaceSettings request, CancellationToken cancellationToken)
    {
        WorkspaceSettingsViewModel page = model.WorkspaceSettings;
        if (request.GameFolder is null && request.FafDataFolder is null && request.VaultFolder is null && request.AutoGenerate is null && request.OnExport is null)
        {
            await page.OpenAsync();
            return page.Describe();
        }
        ProceduralDefaults? procedural = null;
        if (request.AutoGenerate is not null || request.OnExport is not null)
        {
            ProceduralDefaults current = (page.IsOpen ? page.ProceduralDraft : null) ?? model.Setup.Workspace!.Procedural;
            string[] known = [.. model.Generation.Results.Generators.Select(generator => generator.Id)];
            if ((request.AutoGenerate ?? []).Concat(request.OnExport ?? []).FirstOrDefault(id => !known.Contains(id)) is string unknown)
            {
                throw new ArgumentException($"There is no generator '{unknown}': {string.Join(", ", known)}");
            }
            if ((request.OnExport ?? []).FirstOrDefault(id => !ProceduralDefaults.ExportChoices.Contains(id)) is string noChoice)
            {
                throw new ArgumentException($"The export has no choice about '{noChoice}'; it has about: {string.Join(", ", ProceduralDefaults.ExportChoices)}");
            }
            procedural = current with
            {
                AutoGenerate = request.AutoGenerate is null ? current.AutoGenerate : System.Collections.Immutable.ImmutableSortedSet.CreateRange(StringComparer.Ordinal, request.AutoGenerate),
                OnExport = request.OnExport is null ? current.OnExport : System.Collections.Immutable.ImmutableSortedSet.CreateRange(StringComparer.Ordinal, request.OnExport),
            };
        }
        if (!await page.ApplyAsync(request.GameFolder, request.FafDataFolder, request.VaultFolder, procedural))
        {
            throw new ArgumentException(page.Error);
        }
        model.Status = page.AssetsCurrent ? "Saved the workspace's settings"
            : "Saved the workspace's settings; the assets are to be converted again (File, Workspace settings, Convert again)";
        return page.Describe();
    }
}
