using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Versioning;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// Version control of the open project (user story F-18): one git repository per project, turned
/// on by the map maker's choice on the version control page, then a commit beside each save.
/// </summary>
public sealed partial class MainViewModel
{
    private ILogger VersionControlLog => Logger("VersionControl");

    /// <summary>
    /// Whether the version control page shows: the setup until the project is a repository, then
    /// the commit.
    /// </summary>
    [ObservableProperty]
    public partial bool IsVersionControlOpen { get; set; }

    /// <summary>
    /// How the project stands in git, read when the page opens and after each operation.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVersioned), nameof(IsNotVersioned), nameof(RepositorySummary), nameof(LfsNote))]
    public partial RepositoryStatus? Repository { get; private set; }

    public bool IsVersioned => Repository?.IsRepository == true;

    public bool IsNotVersioned => !IsVersioned;

    /// <summary>
    /// The author of the commits, suggested from git's own settings.
    /// </summary>
    [ObservableProperty]
    public partial string GitAuthorName { get; set; } = "";

    [ObservableProperty]
    public partial string GitAuthorEmail { get; set; } = "";

    /// <summary>
    /// Whether the images and the procedural sources go to Git LFS; on unless the map maker turns it off.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LfsNote))]
    public partial bool GitUseLfs { get; set; } = true;

    /// <summary>
    /// The address of a repository to share the project through; empty for none.
    /// </summary>
    [ObservableProperty]
    public partial string GitRemote { get; set; } = "";

    /// <summary>
    /// What the next commit changes, in the map maker's words.
    /// </summary>
    [ObservableProperty]
    public partial string CommitMessage { get; set; } = "";

    /// <summary>
    /// Why the last operation of the page failed, shown on it.
    /// </summary>
    [ObservableProperty]
    public partial string? VersionControlError { get; private set; }

    /// <summary>
    /// Whether the open map is a project on disk, which a repository can hold.
    /// </summary>
    public bool CanCommit => CanSave && Map?.ProjectFolder is not null;

    public string RepositorySummary => Repository switch
    {
        { IsRepository: true } status => string.Join(" · ", new[]
        {
            status.Branch is string branch ? $"On {branch}" : null,
            Map?.Session.IsModified == true ? "unsaved changes, saved before the commit" : null,
            status.Changes switch
            {
                0 when Map?.Session.IsModified == true => null,
                0 => "nothing changed since the last commit",
                1 => "1 file changed",
                int count => $"{count} files changed",
            },
            status.LastCommit is string last ? $"last commit {last}" : null,
            status.UsesLfs ? "images in Git LFS" : null,
            status.Remote is string remote ? $"shared through {remote}" : "not shared",
        }.Where(part => part is not null)),
        _ => "",
    };

    public string LfsNote => Repository is { LfsInstalled: false } && GitUseLfs
        ? "Git LFS is not installed on this computer: install it from git-lfs.com, or turn this off."
        : "Images, raw heightmaps and the World Machine and Gaea files go to Git LFS: every saved version stays in the history without making the repository heavy.";

    /// <summary>
    /// Opens the version control page for the open project: the setup when it is no repository
    /// yet, with the author git knows; otherwise the commit.
    /// </summary>
    public async Task OpenVersionControlAsync()
    {
        if (Map?.ProjectFolder is not string folder)
        {
            Status = "Save the map as a project first: version control keeps the project's folder";
            return;
        }
        VersionControlError = null;
        RepositoryStatus status = await Task.Run(() => ProjectRepository.Status(folder));
        Repository = status;
        // the same status still reads differently with the map's unsaved changes
        OnPropertyChanged(nameof(RepositorySummary));
        if (!status.GitInstalled)
        {
            VersionControlError = "Git is not installed on this computer: install it from git-scm.com.";
        }
        else if (!status.IsRepository && GitAuthorName.Length == 0 && GitAuthorEmail.Length == 0)
        {
            (GitAuthorName, GitAuthorEmail) = await Task.Run(() => ProjectRepository.DefaultAuthor(folder));
        }
        IsVersionControlOpen = true;
    }

    /// <summary>
    /// Makes the project a repository with the page's choices and commits it as it is, saved first.
    /// </summary>
    public Task EnableVersionControlAsync() => BusyAsync("git-enable", "Turning on version control...", async () =>
    {
        EditedMap map = RequireMap();
        string folder = map.ProjectFolder ?? throw new InvalidOperationException("Save the map as a project first");
        if (map.Session.IsModified)
        {
            await map.SaveAsync();
        }
        RepositorySettings settings = new RepositorySettings { Name = GitAuthorName, Email = GitAuthorEmail, Lfs = GitUseLfs, Remote = GitRemote };
        await Task.Run(() => ProjectRepository.Enable(folder, settings));
        Repository = await Task.Run(() => ProjectRepository.Status(folder));
        VersionControlLog.LogInformation("Version control is on (LFS {Lfs}, shared {Shared})", settings.Lfs, settings.Remote.Length > 0);
        Refresh($"Version control is on: {Repository.LastCommit}");
    });

    /// <summary>
    /// Saves the map when it has changes, then commits everything that changed in the project.
    /// </summary>
    /// <param name="message">What changed; the page's <see cref="CommitMessage"/> when left out.</param>
    /// <returns>The commit's short hash, or <c>null</c> when nothing changed.</returns>
    public async Task<string?> CommitAsync(string? message = null)
    {
        string? commit = null;
        await BusyAsync("commit", "Committing...", async () =>
        {
            EditedMap map = RequireMap();
            string folder = map.ProjectFolder ?? throw new InvalidOperationException("Save the map as a project first");
            string text = message ?? CommitMessage;
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new MapProjectException("Say what the commit changes");
            }
            if (map.Session.IsModified)
            {
                await map.SaveAsync();
            }
            commit = await Task.Run(() => ProjectRepository.Commit(folder, text));
            Repository = await Task.Run(() => ProjectRepository.Status(folder));
            if (commit is not null)
            {
                CommitMessage = "";
                VersionControlLog.LogInformation("Committed {Commit}", commit);
            }
            Refresh(commit is null ? "Nothing changed since the last commit" : $"Committed {commit}: {text.Trim()}");
        });
        return commit;
    }

    [RelayCommand]
    private Task TurnOnVersionControl() => OnPageAsync(async () =>
    {
        await EnableVersionControlAsync();
        IsVersionControlOpen = false;
    });

    [RelayCommand]
    private Task CommitChanges() => OnPageAsync(async () =>
    {
        if (await CommitAsync() is not null)
        {
            IsVersionControlOpen = false;
        }
    });

    [RelayCommand]
    private void CloseVersionControl() => IsVersionControlOpen = false;

    /// <summary>
    /// Runs an operation of the page, showing on it why it failed.
    /// </summary>
    private Task OnPageAsync(Func<Task> operation) => TryAsync(async () =>
    {
        VersionControlError = null;
        try
        {
            await operation();
        }
        catch (Exception exception)
        {
            VersionControlError = exception.Message;
            throw;
        }
    });
}
