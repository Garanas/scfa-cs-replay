using System.Globalization;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.Project.Workspaces;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// File, Workspace settings (user story F-5): a page over the window that edits where the workspace
/// finds the game, FAF's data and the vault maps are exported to (<c>workspace.json</c>), and shows
/// the converted assets (their format, when they were converted, whether they are current) with
/// Convert again, and the Procedural defaults for new and imported maps; it links to the plugins and
/// diagnostics pages. The page saves one way: the folders and the defaults are changed on the page
/// and written together by its Save (Revert puts both back).
/// </summary>
/// <param name="setup">The workspace and its conversion.</param>
/// <param name="run">Runs a command with arguments, as a button does.</param>
public sealed partial class WorkspaceSettingsViewModel(WorkspaceSetupViewModel setup, IEditorDialogs dialogs, ILogger log, Func<string, JsonObject, Task> run) : ObservableObject
{
    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    /// <summary>
    /// The workspace's folder, which the page does not change.
    /// </summary>
    [ObservableProperty]
    public partial string Folder { get; private set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChanges), nameof(ChangesText))]
    public partial string GameFolder { get; set; } = "";

    /// <summary>
    /// FAF's <c>gamedata</c> folder; empty without FAF.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChanges), nameof(ChangesText))]
    public partial string FafDataFolder { get; set; } = "";

    /// <summary>
    /// The vault; maps are exported into its <c>maps</c> folder.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChanges), nameof(ChangesText))]
    public partial string VaultFolder { get; set; } = "";

    /// <summary>
    /// The Procedural defaults as the page's check boxes have them, written by Save; null while no
    /// workspace is set up.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChanges), nameof(ChangesText))]
    public partial ProceduralDefaults? ProceduralDraft { get; private set; }

    /// <summary>
    /// Whether the page holds changes Save has not written: a folder or a Procedural default.
    /// </summary>
    public bool HasChanges => setup.Workspace is Workspace workspace
        && (GameFolder != workspace.GameFolder || FafDataFolder != (workspace.FafDataFolder ?? "") || VaultFolder != workspace.VaultFolder
            || (ProceduralDraft is not null && !ProceduralDraft.Equals(workspace.Procedural)));

    /// <summary>
    /// The footer's line: what Save writes, or that everything is saved.
    /// </summary>
    public string ChangesText => HasChanges
        ? "Not saved yet: Save writes the folders and the Procedural defaults into workspace.json; Revert puts them back."
        : "Everything on this page is saved in workspace.json.";

    /// <summary>
    /// Turns a Procedural default on the page on or off (written by Save).
    /// </summary>
    /// <param name="auto">Whether new maps regenerate it on their own; null leaves it.</param>
    /// <param name="onExport">Whether their export makes or writes it; null leaves it.</param>
    public void ChangeProcedural(string id, bool? auto, bool? onExport)
    {
        if (ProceduralDraft is not ProceduralDefaults draft)
        {
            return;
        }
        if (auto is bool a)
        {
            draft = draft.WithAutoGenerate(id, a);
        }
        if (onExport is bool e)
        {
            draft = draft.WithOnExport(id, e);
        }
        ProceduralDraft = draft;
    }

    /// <summary>
    /// The workspace's Procedural defaults were written outside the page (Use as workspace default,
    /// <c>workspace.setProcedural</c>): the page takes what was written, keeping its other changes.
    /// </summary>
    /// <param name="id">The one result written; null when all were.</param>
    internal void ProceduralSaved(ProceduralDefaults saved, string? id)
    {
        if (id is null || ProceduralDraft is not ProceduralDefaults draft)
        {
            ProceduralDraft = saved;
            return;
        }
        draft = draft.WithAutoGenerate(id, saved.RegeneratesOnItsOwn(id));
        ProceduralDraft = ProceduralDefaults.ExportChoices.Contains(id) ? draft.WithOnExport(id, saved.OnExport.Contains(id)) : draft;
    }

    /// <summary>
    /// What is wrong with the folders as typed; null when nothing is.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; private set; }

    public bool HasError => Error is not null;

    /// <summary>
    /// The assets' format, the editor that converted them and when; or why there are none.
    /// </summary>
    [ObservableProperty]
    public partial string AssetsText { get; private set; } = "";

    /// <summary>
    /// What was converted: how many textures, meshes, props and units.
    /// </summary>
    [ObservableProperty]
    public partial string AssetsCounts { get; private set; } = "";

    /// <summary>
    /// Whether the assets are current, or why they are to be converted again.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOutdated))]
    public partial string AssetsState { get; private set; } = "";

    /// <summary>
    /// Whether the assets are to be converted again (the game's or FAF's archives, or the editor, changed).
    /// </summary>
    [ObservableProperty]
    public partial bool AssetsCurrent { get; private set; } = true;

    public bool IsOutdated => !AssetsCurrent;

    /// <summary>
    /// The check of the assets as the page last read it.
    /// </summary>
    public AssetCheck? Check { get; private set; }

    /// <summary>
    /// Opens the page with the workspace's folders as they are, and checks the assets on a thread
    /// of the pool (FAF's archives are compared when they changed).
    /// </summary>
    /// <exception cref="InvalidOperationException">No workspace is set up.</exception>
    public async Task OpenAsync()
    {
        Workspace workspace = setup.Workspace ?? throw new InvalidOperationException("Set up a workspace first");
        Fill(workspace);
        Error = null;
        IsOpen = true;
        await CheckAssetsAsync();
    }

    private void Fill(Workspace workspace)
    {
        Folder = workspace.Folder;
        GameFolder = workspace.GameFolder;
        FafDataFolder = workspace.FafDataFolder ?? "";
        VaultFolder = workspace.VaultFolder;
        ProceduralDraft = workspace.Procedural;
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(ChangesText));
    }

    /// <summary>
    /// Another workspace was set up: the page shows its settings.
    /// </summary>
    internal void WorkspaceChanged()
    {
        if (setup.Workspace is Workspace workspace)
        {
            Fill(workspace);
        }
        else
        {
            ProceduralDraft = null;
        }
    }

    /// <summary>
    /// Reads the assets' manifest and whether they are current.
    /// </summary>
    public async Task CheckAssetsAsync()
    {
        if (setup.Workspace is not Workspace workspace)
        {
            return;
        }
        AssetCheck check = await Task.Run(workspace.CheckAssets);
        Check = check;
        AssetsCurrent = check.IsCurrent;
        AssetsState = check.IsCurrent ? "Current: converted from the archives as they are, by this editor."
            : string.Join(" ", check.Reasons);
        if (check.Manifest is { } manifest)
        {
            AssetsText = string.Create(CultureInfo.InvariantCulture,
                $"Format {manifest.FormatVersion} (this editor writes {AssetConverter.FormatVersion}), converted by the editor {manifest.EditorVersion} on {manifest.Converted.ToLocalTime():yyyy-MM-dd HH:mm}");
            AssetsCounts = string.Create(CultureInfo.InvariantCulture,
                $"{manifest.Textures} textures, {manifest.Meshes} meshes, {manifest.Props} props, {manifest.Units} units from {manifest.Archives.Count} archives");
        }
        else
        {
            AssetsText = "Not converted yet.";
            AssetsCounts = "";
        }
    }

    /// <summary>
    /// Writes the folders as typed and the Procedural defaults into the workspace (through
    /// <c>file.workspaceSettings</c>, which automation runs with them as its arguments).
    /// </summary>
    /// <param name="procedural">The Procedural defaults to write; null writes the page's (while it is open), else keeps the workspace's.</param>
    /// <returns>Whether they were written; <see cref="Error"/> says why not.</returns>
    public async Task<bool> ApplyAsync(string? gameFolder = null, string? fafDataFolder = null, string? vaultFolder = null, ProceduralDefaults? procedural = null)
    {
        Workspace workspace = setup.Workspace ?? throw new InvalidOperationException("Set up a workspace first");
        string game = gameFolder ?? (IsOpen ? GameFolder : workspace.GameFolder);
        string? faf = fafDataFolder ?? (IsOpen ? FafDataFolder : workspace.FafDataFolder);
        string vault = vaultFolder ?? (IsOpen ? VaultFolder : workspace.VaultFolder);
        ProceduralDefaults defaults = procedural ?? (IsOpen ? ProceduralDraft : null) ?? workspace.Procedural;
        try
        {
            if (game != workspace.GameFolder || (faf ?? "") != (workspace.FafDataFolder ?? "") || vault != workspace.VaultFolder)
            {
                setup.ChangeFolders(game, faf, vault);
            }
            if (!defaults.Equals(setup.Workspace!.Procedural))
            {
                setup.ChangeProcedural(defaults);
            }
            Fill(setup.Workspace!);
            Error = null;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or Project.MapProjectException)
        {
            Error = exception.Message;
            log.LogError(exception, "Changing the workspace's folders failed: {Reason}", exception.Message);
            return false;
        }
        await CheckAssetsAsync();
        return true;
    }

    /// <summary>
    /// The page's Save: writes the folders as typed and the Procedural defaults as checked.
    /// </summary>
    [RelayCommand]
    private Task Save() => run("file.workspaceSettings", new JsonObject
    {
        ["gameFolder"] = GameFolder,
        ["fafDataFolder"] = FafDataFolder,
        ["vaultFolder"] = VaultFolder,
        ["autoGenerate"] = new JsonArray([.. (ProceduralDraft?.AutoGenerate ?? []).Select(id => (JsonNode)JsonValue.Create(id))]),
        ["onExport"] = new JsonArray([.. (ProceduralDraft?.OnExport ?? []).Select(id => (JsonNode)JsonValue.Create(id))]),
    });

    /// <summary>
    /// Puts the fields back as the workspace has them.
    /// </summary>
    [RelayCommand]
    private void Revert()
    {
        if (setup.Workspace is Workspace workspace)
        {
            Fill(workspace);
            Error = null;
        }
    }

    [RelayCommand]
    private void Close() => IsOpen = false;

    /// <summary>
    /// Converts the assets again; the editor waits until it is done, then the page shows the new state.
    /// </summary>
    [RelayCommand]
    private async Task ConvertAgain()
    {
        IsOpen = false;
        await run("workspace.convert", []);
    }

    [RelayCommand]
    private Task OpenPlugins()
    {
        IsOpen = false;
        return run("plugins.manage", []);
    }

    [RelayCommand]
    private Task OpenDiagnostics()
    {
        IsOpen = false;
        return run("help.diagnostics", []);
    }

    [RelayCommand]
    private async Task BrowseGameFolder()
    {
        if (await dialogs.PickFolderAsync("The folder the game is installed in", GameFolder) is string folder)
        {
            GameFolder = folder;
        }
    }

    [RelayCommand]
    private async Task BrowseFafDataFolder()
    {
        if (await dialogs.PickFolderAsync("FAF's gamedata folder", FafDataFolder) is string folder)
        {
            FafDataFolder = folder;
        }
    }

    [RelayCommand]
    private async Task BrowseVaultFolder()
    {
        if (await dialogs.PickFolderAsync("The vault folder", VaultFolder) is string folder)
        {
            VaultFolder = folder;
        }
    }

    /// <summary>
    /// The page for automation.
    /// </summary>
    public JsonObject Describe() => new JsonObject
    {
        ["open"] = IsOpen,
        ["folder"] = setup.Workspace?.Folder,
        ["gameFolder"] = setup.Workspace?.GameFolder,
        ["fafDataFolder"] = setup.Workspace?.FafDataFolder,
        ["vaultFolder"] = setup.Workspace?.VaultFolder,
        ["procedural"] = setup.Workspace is Workspace workspace ? Commands.WorkspaceProceduralHandler.Describe(workspace.Procedural) : null,
        ["exportFolder"] = setup.Workspace?.MapsFolder,
        ["error"] = Error,
        ["assets"] = AssetsText,
        ["counts"] = AssetsCounts,
        ["current"] = Check?.IsCurrent,
        ["state"] = AssetsState,
    };
}
