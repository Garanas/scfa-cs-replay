using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Workspaces;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// What the window shows: the setup of a workspace, the question to update its assets, the
/// conversion, or the editor.
/// </summary>
public enum EditorStage
{
    /// <summary>
    /// No workspace is set up, or the remembered one is gone: the person sets one up.
    /// </summary>
    SetUp,

    /// <summary>
    /// The workspace's assets need converting (again); the editor waits until they are.
    /// </summary>
    UpdateAssets,

    /// <summary>
    /// Converting the game's assets; the editor waits until it is done.
    /// </summary>
    Converting,

    Ready,
}

/// <summary>
/// The start of the editor (user story F-5): the workspace remembered, or its setup page; checking
/// and converting the game's assets into it. Raises <see cref="WorkspaceReady"/> once the editor
/// can work in it.
/// </summary>
/// <param name="dialogs">Asks for the folders of the Browse buttons.</param>
/// <param name="status">Writes the editor's status line.</param>
public sealed partial class WorkspaceSetupViewModel(IEditorDialogs dialogs, ILogger log, Action<string> status) : ObservableObject
{
    private UserSettings? _settings;
    private Workspace? _pending;
    private readonly CancellationTokenSource _closing = new CancellationTokenSource();

    /// <summary>
    /// Reads FAF's <c>fa_path.lua</c> for what the setup suggests; tests give their own.
    /// </summary>
    public Func<FafInstallation?> FindFaf { get; set; } = () => FafInstallation.Find();

    /// <summary>
    /// Raised when the workspace is set up and its assets are converted.
    /// </summary>
    public event Action<Workspace>? WorkspaceReady;

    /// <summary>
    /// The workspace, once it is set up and its assets are converted.
    /// </summary>
    [ObservableProperty]
    public partial Workspace? Workspace { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSettingUp), nameof(IsAskingToUpdate), nameof(IsConverting), nameof(IsReady))]
    public partial EditorStage Stage { get; private set; } = EditorStage.Ready;

    public bool IsSettingUp => Stage == EditorStage.SetUp;

    public bool IsAskingToUpdate => Stage == EditorStage.UpdateAssets;

    public bool IsConverting => Stage == EditorStage.Converting;

    public bool IsReady => Stage == EditorStage.Ready;

    /// <summary>
    /// Where the workspace goes; a folder that holds one already is used as it is.
    /// </summary>
    [ObservableProperty]
    public partial string Folder { get; set; } = "";

    /// <summary>
    /// The game's installation, with <c>gamedata/env.scd</c> and <c>gamedata/units.scd</c>.
    /// </summary>
    [ObservableProperty]
    public partial string GameFolder { get; set; } = "";

    /// <summary>
    /// FAF's <c>gamedata</c> folder; empty without FAF.
    /// </summary>
    [ObservableProperty]
    public partial string FafDataFolder { get; set; } = "";

    /// <summary>
    /// The vault on disk, where maps are exported to.
    /// </summary>
    [ObservableProperty]
    public partial string VaultFolder { get; set; } = "";

    /// <summary>
    /// What is wrong with the setup or the conversion, shown on its page.
    /// </summary>
    [ObservableProperty]
    public partial string? Error { get; set; }

    /// <summary>
    /// Why the assets need converting again.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<string> AssetReasons { get; private set; } = [];

    /// <summary>
    /// How far the conversion is, from 0 to 1.
    /// </summary>
    [ObservableProperty]
    public partial double ConversionFraction { get; private set; }

    [ObservableProperty]
    public partial string ConversionText { get; private set; } = "";

    /// <summary>
    /// Starts with the workspace remembered in <paramref name="settings"/>, or asks to set one up.
    /// </summary>
    public async Task StartAsync(UserSettings settings)
    {
        _settings = settings;
        if (settings.Workspace is string folder && Workspace.Exists(folder))
        {
            try
            {
                await UseAsync(Workspace.Open(folder), convertWhenMissing: false);
                return;
            }
            catch (MapProjectException exception)
            {
                Error = exception.Message;
                log.LogError(exception, "The workspace {Folder} could not be opened: {Reason}", folder, exception.Message);
            }
        }
        Show();
    }

    /// <summary>
    /// Shows the setup, filled in from FAF's <c>fa_path.lua</c> where it can.
    /// </summary>
    public void Show()
    {
        FafInstallation? faf = FindFaf();
        Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "FAF Map Editor");
        GameFolder = faf?.GameFolder ?? "";
        FafDataFolder = faf?.DataFolder ?? "";
        VaultFolder = faf?.VaultFolder ?? "";
        Stage = EditorStage.SetUp;
        status("Set up a workspace to start.");
    }

    /// <summary>
    /// Sets up the workspace as filled in (<see cref="Folder"/>, ...), remembers it and converts the
    /// game's assets into it.
    /// </summary>
    /// <returns>Whether it is set up; <see cref="Error"/> says why not.</returns>
    public async Task<bool> SetUpAsync()
    {
        Error = Validate();
        if (Error is not null)
        {
            return false;
        }

        Workspace workspace;
        try
        {
            workspace = Workspace.Exists(Folder) && string.IsNullOrWhiteSpace(GameFolder)
                ? Workspace.Open(Folder)
                : Workspace.Create(Folder, GameFolder, NullIfEmpty(FafDataFolder), VaultFolder);
        }
        catch (Exception exception) when (exception is MapProjectException or IOException or UnauthorizedAccessException)
        {
            Error = exception.Message;
            log.LogError(exception, "Setting up the workspace {Folder} failed: {Reason}", Folder, exception.Message);
            return false;
        }
        try
        {
            _settings?.SetWorkspace(workspace.Folder);
        }
        catch (Exception exception) when (exception is MapProjectException or IOException or UnauthorizedAccessException)
        {
            // the workspace is used now; the next start asks for it again
            log.LogWarning(exception, "The workspace {Folder} could not be remembered: {Reason}", workspace.Folder, exception.Message);
        }
        await UseAsync(workspace, convertWhenMissing: true);
        return true;
    }

    /// <summary>
    /// Converts the workspace's assets; the editor waits until they are done.
    /// </summary>
    /// <returns>Whether the conversion finished.</returns>
    public async Task<bool> ConvertAssetsAsync()
    {
        Workspace workspace = _pending ?? Workspace ?? throw new InvalidOperationException("No workspace is set up");
        Stage = EditorStage.Converting;
        Error = null;
        ConversionFraction = 0;
        ConversionText = "Starting";
        status($"Converting the game's assets into {workspace.AssetsFolder}...");

        Progress<AssetProgress> progress = new Progress<AssetProgress>(p =>
        {
            ConversionFraction = p.Fraction;
            ConversionText = p.Total > 1 ? $"{p.Stage}: {p.Done} of {p.Total} files" : p.Stage;
        });
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        double Milliseconds() => Math.Round(System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        try
        {
            AssetConversion conversion = await AssetConverter.ConvertAsync(workspace, progress, _closing.Token);
            double milliseconds = Milliseconds();
            EditorMetrics.OperationDuration.Record(milliseconds, EditorMetrics.Tag("operation", "convert"), EditorMetrics.Tag("result", "ok"));
            log.LogInformation("Converted the game's assets in {Milliseconds} ms: {Textures} textures, {Props} props, {Units} units",
                milliseconds, conversion.Manifest.Textures, conversion.Manifest.Props, conversion.Manifest.Units);
            if (conversion.Skipped.Count > 0)
            {
                log.LogWarning("Skipped {Count} files while converting the game's assets, such as {Examples}",
                    conversion.Skipped.Count, string.Join("; ", conversion.Skipped.Take(5)));
            }
            Ready(workspace, $"Converted the game's assets: {conversion.Manifest.Textures} textures, {conversion.Manifest.Props} props, " +
                $"{conversion.Manifest.Units} units{(conversion.Skipped.Count > 0 ? $"; {conversion.Skipped.Count} files skipped" : "")}");
            return true;
        }
        catch (OperationCanceledException)
        {
            log.LogInformation("Converting the game's assets stopped after {Milliseconds} ms", Milliseconds());
            return false;
        }
        catch (Exception exception) when (exception is MapProjectException or IOException or UnauthorizedAccessException)
        {
            EditorMetrics.OperationDuration.Record(Milliseconds(), EditorMetrics.Tag("operation", "convert"), EditorMetrics.Tag("result", "failed"));
            log.LogError(exception, "Converting the game's assets failed: {Reason}", exception.Message);
            AssetReasons = [exception.Message];
            Stage = EditorStage.UpdateAssets;
            Error = exception.Message;
            return false;
        }
    }

    /// <summary>
    /// Stops a conversion when the window closes; the next start goes on where it was.
    /// </summary>
    public void StopWork() => _closing.Cancel();

    private async Task UseAsync(Workspace workspace, bool convertWhenMissing)
    {
        // reading FAF's archives for their checksum takes a moment
        AssetCheck check = await Task.Run(workspace.CheckAssets);
        if (check.IsCurrent)
        {
            Ready(workspace, $"Workspace {workspace.Folder}");
            return;
        }

        _pending = workspace;
        AssetReasons = check.Reasons;
        log.LogInformation("The game's assets are to be converted ({State}): {Reasons}", check.State, string.Join("; ", check.Reasons));
        if (convertWhenMissing && check.State == AssetState.Missing)
        {
            await ConvertAssetsAsync();
        }
        else
        {
            Stage = EditorStage.UpdateAssets;
            status("The game's assets need to be converted again.");
        }
    }

    /// <summary>
    /// Works in a workspace that is set up, without checking or converting its assets: for tests of
    /// what keeps files in the workspace (stamps, templates, stars and recents).
    /// </summary>
    public void Use(Workspace workspace) => Ready(workspace, $"Workspace {workspace.Folder}");

    private void Ready(Workspace workspace, string text)
    {
        _pending = null;
        Workspace = workspace;
        log.LogInformation("Working in {Folder}", workspace.Folder);
        Stage = EditorStage.Ready;
        status(text);
        WorkspaceReady?.Invoke(workspace);
    }

    private string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Folder))
        {
            return "Choose a folder for the workspace.";
        }
        if (Workspace.Exists(Folder) && string.IsNullOrWhiteSpace(GameFolder))
        {
            return null;
        }
        if (Directory.Exists(Folder) && !Workspace.Exists(Folder) && Directory.EnumerateFileSystemEntries(Folder).Any())
        {
            return $"{Folder} holds other files: choose an empty folder or an existing workspace.";
        }
        return ValidateFolders(GameFolder, FafDataFolder, VaultFolder);
    }

    /// <summary>
    /// What is wrong with the folders of a workspace: the game's must hold its archives, FAF's data
    /// folder (optional) and the vault must exist; null when nothing is.
    /// </summary>
    public static string? ValidateFolders(string gameFolder, string? fafDataFolder, string vaultFolder)
    {
        foreach (string name in GameArchives.Names)
        {
            if (!File.Exists(Path.Combine(gameFolder, "gamedata", name + ".scd")))
            {
                return $"The game folder has no gamedata\\{name}.scd: choose the folder Supreme Commander: Forged Alliance is installed in.";
            }
        }
        if (!string.IsNullOrWhiteSpace(fafDataFolder) && !Directory.Exists(fafDataFolder))
        {
            return $"FAF's data folder {fafDataFolder} does not exist; leave it empty without FAF.";
        }
        if (string.IsNullOrWhiteSpace(vaultFolder) || !Directory.Exists(vaultFolder))
        {
            return "Choose the vault folder: the one the FAF client keeps maps and mods in.";
        }
        return null;
    }

    /// <summary>
    /// Changes where the workspace finds the game, FAF's data and the vault (File, Workspace
    /// settings), writing its <c>workspace.json</c>. Its assets stay as they are: a game or FAF
    /// folder of other archives makes them out of date, and converting them again is asked for.
    /// </summary>
    /// <returns>The workspace as it is now.</returns>
    /// <exception cref="InvalidOperationException">No workspace is set up.</exception>
    /// <exception cref="ArgumentException">A folder is not what it should be.</exception>
    public Workspace ChangeFolders(string gameFolder, string? fafDataFolder, string vaultFolder)
    {
        Workspace workspace = Workspace ?? throw new InvalidOperationException("No workspace is set up");
        if (ValidateFolders(gameFolder, fafDataFolder, vaultFolder) is string problem)
        {
            throw new ArgumentException(problem);
        }
        Workspace changed = workspace with
        {
            GameFolder = Path.GetFullPath(gameFolder),
            FafDataFolder = string.IsNullOrWhiteSpace(fafDataFolder) ? null : Path.GetFullPath(fafDataFolder),
            VaultFolder = Path.GetFullPath(vaultFolder),
        };
        changed.Save();
        Workspace = changed;
        log.LogInformation("The workspace {Folder} finds the game in {Game}, FAF's data in {Faf} and the vault in {Vault}",
            changed.Folder, changed.GameFolder, changed.FafDataFolder ?? "(none)", changed.VaultFolder);
        return changed;
    }

    private static string? NullIfEmpty(string text) => string.IsNullOrWhiteSpace(text) ? null : text;

    [RelayCommand]
    private async Task BrowseFolder()
    {
        if (await dialogs.PickFolderAsync("The folder of the workspace", Folder) is string folder)
        {
            Folder = folder;
            if (Workspace.Exists(folder))
            {
                try
                {
                    Workspace existing = Workspace.Open(folder);
                    GameFolder = existing.GameFolder;
                    FafDataFolder = existing.FafDataFolder ?? "";
                    VaultFolder = existing.VaultFolder;
                }
                catch (MapProjectException exception)
                {
                    Error = exception.Message;
                }
            }
        }
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
}
