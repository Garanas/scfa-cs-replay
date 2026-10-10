using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Workspaces;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Shows the workspace's folder (its projects, assets and settings) in the system's file manager
/// (user story F-5).
/// </summary>
[EditorCommand("help.workspaceFolder", "Show the workspace folder", Story = "F-5", Needs = Needs.Workspace)]
public sealed record ShowWorkspaceFolder : IRequest;

public sealed class ShowWorkspaceFolderHandler(MainViewModel model, WorkspaceSetupViewModel setup) : IRequestHandler<ShowWorkspaceFolder>
{
    public Task Handle(ShowWorkspaceFolder request, CancellationToken cancellationToken)
    {
        if (setup.Workspace is { } workspace)
        {
            model.ShowFolder(workspace.Folder);
        }
        return Task.CompletedTask;
    }
}

/// <summary>
/// Shows the folder with the sessions' logs and metrics (user story F-17).
/// </summary>
[EditorCommand("help.diagnosticsFolder", "Show the diagnostics folder", Story = "F-17")]
public sealed record ShowDiagnosticsFolder : IRequest;

public sealed class ShowDiagnosticsFolderHandler(DiagnosticsViewModel diagnostics) : IRequestHandler<ShowDiagnosticsFolder>
{
    public Task Handle(ShowDiagnosticsFolder request, CancellationToken cancellationToken)
    {
        diagnostics.ShowFolder();
        return Task.CompletedTask;
    }
}

/// <summary>
/// The Help menu's Folders: each shows one folder in the system's file manager and answers its path
/// (for automation). A folder of the workspace is made when it is missing; the game's and FAF's are
/// only shown when they are there.
/// </summary>
public static class Folders
{
    /// <summary>
    /// Shows a folder of the workspace, made first when it is missing.
    /// </summary>
    public static Task<string> ShowMade(MainViewModel model, string folder)
    {
        Directory.CreateDirectory(folder);
        model.ShowFolder(folder);
        return Task.FromResult(folder);
    }

    /// <summary>
    /// Shows a folder that is not the editor's to make (the game's, FAF's).
    /// </summary>
    /// <exception cref="ArgumentException">It is not there.</exception>
    public static Task<string> ShowExisting(MainViewModel model, string? folder, string what)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            throw new ArgumentException(string.IsNullOrEmpty(folder) ? $"The workspace names no {what}" : $"The {what} {folder} is not there");
        }
        model.ShowFolder(folder);
        return Task.FromResult(folder);
    }

    public static Workspace WorkspaceOf(WorkspaceSetupViewModel setup) => setup.Workspace ?? throw new InvalidOperationException("Set up a workspace first");
}

/// <summary>
/// Shows the workspace's map projects (user story F-5).
/// </summary>
[EditorCommand("help.projectsFolder", "Show the projects folder", Story = "F-5", Needs = Needs.Workspace)]
public sealed record ShowProjectsFolder : IRequest<string>;

public sealed class ShowProjectsFolderHandler(MainViewModel model, WorkspaceSetupViewModel setup) : IRequestHandler<ShowProjectsFolder, string>
{
    public Task<string> Handle(ShowProjectsFolder request, CancellationToken cancellationToken) => Folders.ShowMade(model, Folders.WorkspaceOf(setup).ProjectsFolder);
}

/// <summary>
/// Shows the open map's project folder: its <c>map.json</c>, PNGs and script (user story M-7).
/// </summary>
[EditorCommand("help.projectFolder", "Show the open project's folder", Story = "M-7", Needs = Needs.Project)]
public sealed record ShowProjectFolder : IRequest<string>;

public sealed class ShowProjectFolderHandler(MainViewModel model) : IRequestHandler<ShowProjectFolder, string>
{
    public Task<string> Handle(ShowProjectFolder request, CancellationToken cancellationToken) =>
        Folders.ShowExisting(model, model.Map?.ProjectFolder, "project folder of the open map");
}

/// <summary>
/// Shows the workspace's presets: a folder per kind (brushes, stamps, lighting, water, sky, props,
/// units, markers, selections, maps; user story F-10).
/// </summary>
[EditorCommand("help.presetsFolder", "Show the presets folder", Story = "F-10", Needs = Needs.Workspace)]
public sealed record ShowPresetsFolder : IRequest<string>;

public sealed class ShowPresetsFolderHandler(MainViewModel model, WorkspaceSetupViewModel setup) : IRequestHandler<ShowPresetsFolder, string>
{
    public Task<string> Handle(ShowPresetsFolder request, CancellationToken cancellationToken) => Folders.ShowMade(model, Folders.WorkspaceOf(setup).PresetsFolder);
}

/// <summary>
/// Shows the workspace's converted assets: the game's textures as PNG, blueprints as JSON, meshes as
/// glTF (user story F-8).
/// </summary>
[EditorCommand("help.assetsFolder", "Show the converted assets folder", Story = "F-8", Needs = Needs.Workspace)]
public sealed record ShowAssetsFolder : IRequest<string>;

public sealed class ShowAssetsFolderHandler(MainViewModel model, WorkspaceSetupViewModel setup) : IRequestHandler<ShowAssetsFolder, string>
{
    public Task<string> Handle(ShowAssetsFolder request, CancellationToken cancellationToken) =>
        Folders.ShowExisting(model, Folders.WorkspaceOf(setup).AssetsFolder, "converted assets folder");
}

/// <summary>
/// Shows the folder the game is installed in (user story F-5).
/// </summary>
[EditorCommand("help.gameFolder", "Show the game's folder", Story = "F-5", Needs = Needs.Workspace)]
public sealed record ShowGameFolder : IRequest<string>;

public sealed class ShowGameFolderHandler(MainViewModel model, WorkspaceSetupViewModel setup) : IRequestHandler<ShowGameFolder, string>
{
    public Task<string> Handle(ShowGameFolder request, CancellationToken cancellationToken) =>
        Folders.ShowExisting(model, Folders.WorkspaceOf(setup).GameFolder, "game folder");
}

/// <summary>
/// Shows FAF's data folder, with its <c>.nx2</c> archives (user story F-5).
/// </summary>
[EditorCommand("help.fafDataFolder", "Show FAF's data folder", Story = "F-5", Needs = Needs.Workspace)]
public sealed record ShowFafDataFolder : IRequest<string>;

public sealed class ShowFafDataFolderHandler(MainViewModel model, WorkspaceSetupViewModel setup) : IRequestHandler<ShowFafDataFolder, string>
{
    public Task<string> Handle(ShowFafDataFolder request, CancellationToken cancellationToken) =>
        Folders.ShowExisting(model, Folders.WorkspaceOf(setup).FafDataFolder, "FAF data folder");
}

/// <summary>
/// Shows the folder maps are exported into: the vault's <c>maps</c>, where the game and the FAF
/// client find them (user story M-7).
/// </summary>
[EditorCommand("help.exportFolder", "Show the export folder", Story = "M-7", Needs = Needs.Workspace)]
public sealed record ShowExportFolder : IRequest<string>;

public sealed class ShowExportFolderHandler(MainViewModel model, WorkspaceSetupViewModel setup) : IRequestHandler<ShowExportFolder, string>
{
    public Task<string> Handle(ShowExportFolder request, CancellationToken cancellationToken) =>
        Folders.ShowExisting(model, Folders.WorkspaceOf(setup).MapsFolder, "export folder (the vault's maps)");
}
