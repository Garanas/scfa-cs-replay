using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.App.Diagnostics;
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

public sealed partial class MainViewModel
{
    private UserSettings? _settings;
    private string? _openWhenReady;
    private readonly CancellationTokenSource _closing = new CancellationTokenSource();

    /// <summary>
    /// Reads FAF's <c>fa_path.lua</c> for what the setup suggests; tests give their own.
    /// </summary>
    public Func<FafInstallation?> FindFaf { get; init; } = () => FafInstallation.Find();

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
    public partial string SetupFolder { get; set; } = "";

    /// <summary>
    /// The game's installation, with <c>gamedata/env.scd</c> and <c>gamedata/units.scd</c>.
    /// </summary>
    [ObservableProperty]
    public partial string SetupGameFolder { get; set; } = "";

    /// <summary>
    /// FAF's <c>gamedata</c> folder; empty without FAF.
    /// </summary>
    [ObservableProperty]
    public partial string SetupFafDataFolder { get; set; } = "";

    /// <summary>
    /// The vault on disk, where maps are exported to.
    /// </summary>
    [ObservableProperty]
    public partial string SetupVaultFolder { get; set; } = "";

    /// <summary>
    /// What is wrong with the setup or the conversion, shown on its page.
    /// </summary>
    [ObservableProperty]
    public partial string? SetupError { get; set; }

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
    /// <param name="openFolder">A project or map folder to open once the workspace is ready.</param>
    public async Task StartAsync(UserSettings settings, string? openFolder = null)
    {
        _settings = settings;
        _openWhenReady = openFolder;
        if (settings.Workspace is string folder && Workspace.Exists(folder))
        {
            try
            {
                await UseWorkspaceAsync(Workspace.Open(folder), convertWhenMissing: false);
                return;
            }
            catch (MapProjectException exception)
            {
                SetupError = exception.Message;
                WorkspaceLog.LogError(exception, "The workspace {Folder} could not be opened: {Reason}", folder, exception.Message);
            }
        }
        ShowSetup();
    }

    /// <summary>
    /// Shows the setup, filled in from FAF's <c>fa_path.lua</c> where it can.
    /// </summary>
    public void ShowSetup()
    {
        FafInstallation? faf = FindFaf();
        SetupFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "FAF Map Editor");
        SetupGameFolder = faf?.GameFolder ?? "";
        SetupFafDataFolder = faf?.DataFolder ?? "";
        SetupVaultFolder = faf?.VaultFolder ?? "";
        Stage = EditorStage.SetUp;
        Status = "Set up a workspace to start.";
    }

    /// <summary>
    /// Sets up the workspace as filled in (<see cref="SetupFolder"/>, ...), remembers it and
    /// converts the game's assets into it.
    /// </summary>
    /// <returns>Whether it is set up; <see cref="SetupError"/> says why not.</returns>
    public async Task<bool> SetUpWorkspaceAsync()
    {
        SetupError = Validate();
        if (SetupError is not null)
        {
            return false;
        }

        Workspace workspace;
        try
        {
            workspace = Workspace.Exists(SetupFolder) && string.IsNullOrWhiteSpace(SetupGameFolder)
                ? Workspace.Open(SetupFolder)
                : Workspace.Create(SetupFolder, SetupGameFolder, NullIfEmpty(SetupFafDataFolder), SetupVaultFolder);
        }
        catch (Exception exception) when (exception is MapProjectException or IOException or UnauthorizedAccessException)
        {
            SetupError = exception.Message;
            WorkspaceLog.LogError(exception, "Setting up the workspace {Folder} failed: {Reason}", SetupFolder, exception.Message);
            return false;
        }
        _settings?.SetWorkspace(workspace.Folder);
        await UseWorkspaceAsync(workspace, convertWhenMissing: true);
        return true;
    }

    /// <summary>
    /// Converts the workspace's assets; the editor waits until they are done.
    /// </summary>
    /// <returns>Whether the conversion finished.</returns>
    public async Task<bool> ConvertAssetsAsync()
    {
        Workspace workspace = _pendingWorkspace ?? Workspace ?? throw new InvalidOperationException("No workspace is set up");
        Stage = EditorStage.Converting;
        SetupError = null;
        ConversionFraction = 0;
        ConversionText = "Starting";
        Status = $"Converting the game's assets into {workspace.AssetsFolder}...";

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
            WorkspaceLog.LogInformation("Converted the game's assets in {Milliseconds} ms: {Textures} textures, {Props} props, {Units} units",
                milliseconds, conversion.Manifest.Textures, conversion.Manifest.Props, conversion.Manifest.Units);
            if (conversion.Skipped.Count > 0)
            {
                WorkspaceLog.LogWarning("Skipped {Count} files while converting the game's assets, such as {Examples}",
                    conversion.Skipped.Count, string.Join("; ", conversion.Skipped.Take(5)));
            }
            Ready(workspace, $"Converted the game's assets: {conversion.Manifest.Textures} textures, {conversion.Manifest.Props} props, " +
                $"{conversion.Manifest.Units} units{(conversion.Skipped.Count > 0 ? $"; {conversion.Skipped.Count} files skipped" : "")}");
            return true;
        }
        catch (OperationCanceledException)
        {
            WorkspaceLog.LogInformation("Converting the game's assets stopped after {Milliseconds} ms", Milliseconds());
            return false;
        }
        catch (Exception exception) when (exception is MapProjectException or IOException or UnauthorizedAccessException)
        {
            EditorMetrics.OperationDuration.Record(Milliseconds(), EditorMetrics.Tag("operation", "convert"), EditorMetrics.Tag("result", "failed"));
            WorkspaceLog.LogError(exception, "Converting the game's assets failed: {Reason}", exception.Message);
            AssetReasons = [exception.Message];
            Stage = EditorStage.UpdateAssets;
            SetupError = exception.Message;
            return false;
        }
    }

    /// <summary>
    /// Stops a conversion when the window closes; the next start goes on where it was.
    /// </summary>
    public void StopWork() => _closing.Cancel();

    private Workspace? _pendingWorkspace;

    private async Task UseWorkspaceAsync(Workspace workspace, bool convertWhenMissing)
    {
        // reading FAF's archives for their checksum takes a moment
        AssetCheck check = await Task.Run(workspace.CheckAssets);
        if (check.IsCurrent)
        {
            Ready(workspace, $"Workspace {workspace.Folder}");
            return;
        }

        _pendingWorkspace = workspace;
        AssetReasons = check.Reasons;
        WorkspaceLog.LogInformation("The game's assets are to be converted ({State}): {Reasons}", check.State, string.Join("; ", check.Reasons));
        if (convertWhenMissing && check.State == AssetState.Missing)
        {
            await ConvertAssetsAsync();
        }
        else
        {
            Stage = EditorStage.UpdateAssets;
            Status = "The game's assets need to be converted again.";
        }
    }

    private void Ready(Workspace workspace, string status)
    {
        _pendingWorkspace = null;
        Workspace = workspace;
        WorkspaceLog.LogInformation("Working in {Folder}", workspace.Folder);
        Stage = EditorStage.Ready;
        Status = status;
        if (_openWhenReady is string folder)
        {
            _openWhenReady = null;
            _ = OpenOnStartAsync(folder);
        }
    }

    private string? Validate()
    {
        if (string.IsNullOrWhiteSpace(SetupFolder))
        {
            return "Choose a folder for the workspace.";
        }
        if (Workspace.Exists(SetupFolder) && string.IsNullOrWhiteSpace(SetupGameFolder))
        {
            return null;
        }
        if (Directory.Exists(SetupFolder) && !Workspace.Exists(SetupFolder) && Directory.EnumerateFileSystemEntries(SetupFolder).Any())
        {
            return $"{SetupFolder} holds other files: choose an empty folder or an existing workspace.";
        }
        foreach (string name in GameArchives.Names)
        {
            if (!File.Exists(Path.Combine(SetupGameFolder, "gamedata", name + ".scd")))
            {
                return $"The game folder has no gamedata\\{name}.scd: choose the folder Supreme Commander: Forged Alliance is installed in.";
            }
        }
        if (!string.IsNullOrWhiteSpace(SetupFafDataFolder) && !Directory.Exists(SetupFafDataFolder))
        {
            return $"FAF's data folder {SetupFafDataFolder} does not exist; leave it empty without FAF.";
        }
        if (string.IsNullOrWhiteSpace(SetupVaultFolder) || !Directory.Exists(SetupVaultFolder))
        {
            return "Choose the vault folder: the one the FAF client keeps maps and mods in.";
        }
        return null;
    }

    private static string? NullIfEmpty(string text) => string.IsNullOrWhiteSpace(text) ? null : text;

    [RelayCommand]
    private Task SetUp() => SetUpWorkspaceAsync();

    [RelayCommand]
    private Task UpdateAssets() => ConvertAssetsAsync();

    [RelayCommand]
    private async Task BrowseSetupFolder()
    {
        if (await _dialogs.PickFolderAsync("The folder of the workspace", SetupFolder) is string folder)
        {
            SetupFolder = folder;
            if (Workspace.Exists(folder))
            {
                try
                {
                    Workspace existing = Workspace.Open(folder);
                    SetupGameFolder = existing.GameFolder;
                    SetupFafDataFolder = existing.FafDataFolder ?? "";
                    SetupVaultFolder = existing.VaultFolder;
                }
                catch (MapProjectException exception)
                {
                    SetupError = exception.Message;
                }
            }
        }
    }

    [RelayCommand]
    private async Task BrowseGameFolder()
    {
        if (await _dialogs.PickFolderAsync("The folder the game is installed in", SetupGameFolder) is string folder)
        {
            SetupGameFolder = folder;
        }
    }

    [RelayCommand]
    private async Task BrowseFafDataFolder()
    {
        if (await _dialogs.PickFolderAsync("FAF's gamedata folder", SetupFafDataFolder) is string folder)
        {
            SetupFafDataFolder = folder;
        }
    }

    [RelayCommand]
    private async Task BrowseVaultFolder()
    {
        if (await _dialogs.PickFolderAsync("The vault folder", SetupVaultFolder) is string folder)
        {
            SetupVaultFolder = folder;
        }
    }
}
