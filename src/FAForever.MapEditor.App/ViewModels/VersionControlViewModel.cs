using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Versioning;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// Version control of the open project (user story F-18): one git repository per project, turned
/// on by the map maker's choice on the version control page, then a commit beside each save.
/// </summary>
/// <remarks>
/// It works on the editor's open map: it saves it before a commit, while the editor is busy.
/// </remarks>
public sealed partial class VersionControlViewModel(MainViewModel editor) : ObservableObject
{
    private ILogger Log => editor.Logger("VersionControl");

    /// <summary>
    /// Whether the version control page shows: the setup until the project is a repository, then
    /// the commit.
    /// </summary>
    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    /// <summary>
    /// How the project stands in git, read when the page opens and after each operation.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVersioned), nameof(IsNotVersioned), nameof(Summary), nameof(LfsNote))]
    public partial RepositoryStatus? Repository { get; private set; }

    public bool IsVersioned => Repository?.IsRepository == true;

    public bool IsNotVersioned => !IsVersioned;

    /// <summary>
    /// The author of the commits, suggested from git's own settings.
    /// </summary>
    [ObservableProperty]
    public partial string AuthorName { get; set; } = "";

    [ObservableProperty]
    public partial string AuthorEmail { get; set; } = "";

    /// <summary>
    /// Whether the images and the procedural sources go to Git LFS; on unless the map maker turns it off.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LfsNote))]
    public partial bool UseLfs { get; set; } = true;

    /// <summary>
    /// The address of a repository to share the project through; empty for none.
    /// </summary>
    [ObservableProperty]
    public partial string Remote { get; set; } = "";

    /// <summary>
    /// What the next commit changes, in the map maker's words.
    /// </summary>
    [ObservableProperty]
    public partial string CommitMessage { get; set; } = "";

    /// <summary>
    /// Why the last operation of the page failed, shown on it.
    /// </summary>
    [ObservableProperty]
    public partial string? Error { get; private set; }

    public string Summary => Repository switch
    {
        { IsRepository: true } status => string.Join(" · ", new[]
        {
            status.Branch is string branch ? $"On {branch}" : null,
            editor.Map?.Session.IsModified == true ? "unsaved changes, saved before the commit" : null,
            status.Changes switch
            {
                0 when editor.Map?.Session.IsModified == true => null,
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

    /// <summary>
    /// Whether the project has a repository to push to and pull from.
    /// </summary>
    public bool IsShared => Repository?.Remote is not null;

    /// <summary>
    /// How the project stands against the shared repository, as git knew it when it last talked to it.
    /// </summary>
    public string ShareSummary => Repository switch
    {
        { Remote: null } => "Not shared: give a repository at the setup, or add one as origin with git.",
        { Ahead: null } => "Not pushed yet.",
        { Ahead: 0, Behind: 0 } => "Up to date with the shared repository (when it was last pushed or pulled).",
        { Ahead: int ahead, Behind: int behind } => string.Join(", ", new[]
        {
            ahead > 0 ? $"{ahead} commit{(ahead == 1 ? "" : "s")} to push" : null,
            behind > 0 ? $"{behind} commit{(behind == 1 ? "" : "s")} to pull" : null,
        }.Where(part => part is not null)) + ".",
        _ => "",
    };

    /// <summary>
    /// The commits of the project, newest first, read when the page opens and after each operation.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<CommitInfo> History { get; private set; } = [];

    /// <summary>
    /// The commit chosen in the history, whose files show.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChosenCommit))]
    public partial CommitInfo? ChosenCommit { get; set; }

    public bool HasChosenCommit => ChosenCommit is not null;

    /// <summary>
    /// The files the chosen commit changed.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<CommitFile> ChosenFiles { get; private set; } = [];

    partial void OnRepositoryChanged(RepositoryStatus? value)
    {
        OnPropertyChanged(nameof(IsShared));
        OnPropertyChanged(nameof(ShareSummary));
    }

    partial void OnChosenCommitChanged(CommitInfo? value) => _ = editor.TryAsync(() => ShowFilesAsync(value));

    private async Task ShowFilesAsync(CommitInfo? commit)
    {
        ChosenFiles = [];
        if (commit is not null && editor.Map?.ProjectFolder is string folder)
        {
            IReadOnlyList<CommitFile> files = await Task.Run(() => ProjectRepository.FilesOf(folder, commit.Hash));
            if (ChosenCommit == commit)
            {
                ChosenFiles = files;
            }
        }
    }

    /// <summary>
    /// Reads how the project stands and its history again.
    /// </summary>
    private async Task ReadAsync(string folder)
    {
        Repository = await Task.Run(() => ProjectRepository.Status(folder));
        History = Repository.IsRepository ? await Task.Run(() => ProjectRepository.History(folder)) : [];
        if (ChosenCommit is CommitInfo chosen)
        {
            ChosenCommit = History.FirstOrDefault(commit => commit.Hash == chosen.Hash);
        }
    }

    /// <summary>
    /// The files a commit changed, and chooses it in the history.
    /// </summary>
    /// <exception cref="MapProjectException">There is no such commit.</exception>
    public async Task<IReadOnlyList<CommitFile>> ShowCommitAsync(string commit)
    {
        string folder = RequireFolder();
        if (History.Count == 0)
        {
            await ReadAsync(folder);
        }
        IReadOnlyList<CommitFile> files = await Task.Run(() => ProjectRepository.FilesOf(folder, commit));
        ChosenCommit = History.FirstOrDefault(info => info.Hash.StartsWith(commit, StringComparison.OrdinalIgnoreCase) || info.ShortHash == commit);
        ChosenFiles = files;
        return files;
    }

    /// <summary>
    /// Sends the commits to the shared repository (user story F-18). Signing in is the system's
    /// git credential helper's.
    /// </summary>
    public Task PushAsync() => editor.BusyAsync("git-push", "Pushing...", async () =>
    {
        string folder = RequireFolder();
        await Task.Run(() => ProjectRepository.Push(folder));
        await ReadAsync(folder);
        Log.LogInformation("Pushed {Branch}", Repository?.Branch);
        editor.Status = $"Pushed {Repository?.LastCommit}";
    });

    /// <summary>
    /// Takes the commits of the shared repository (user story F-18), and reads the files that
    /// changed again. Refused while the map has unsaved changes or the project uncommitted ones,
    /// so pulling never overwrites work.
    /// </summary>
    /// <returns>The files that changed.</returns>
    public async Task<IReadOnlyList<string>> PullAsync()
    {
        IReadOnlyList<string> changed = [];
        await editor.BusyAsync("git-pull", "Pulling...", async () =>
        {
            string folder = RequireFolder();
            if (editor.Map?.Session.IsModified == true)
            {
                throw new MapProjectException("The map has unsaved changes: save and commit them first, so that pulling cannot overwrite them");
            }
            changed = await Task.Run(() => ProjectRepository.Pull(folder));
            await ReadAsync(folder);
            editor.ReadAgainFromDisk(changed);
            Log.LogInformation("Pulled {Count} changed files", changed.Count);
            editor.Status = changed.Count == 0 ? "Nothing to pull: up to date" : $"Pulled {changed.Count} changed files: {Repository?.LastCommit}";
        });
        return changed;
    }

    /// <summary>
    /// Goes back to a commit (user story F-18): the map becomes the project as the commit holds
    /// it, as one step of undo, unsaved. The project's files and the history stay as they are, so
    /// nothing is lost: undo comes back, and saving and committing makes it a new commit.
    /// </summary>
    public Task RestoreAsync(string commit) => editor.BusyAsync("git-restore", "Reading the commit...", async () =>
    {
        string folder = RequireFolder();
        MapDocument old = await Task.Run(() => ProjectRepository.ReadAt(folder, commit));
        CommitInfo? info = History.FirstOrDefault(c => c.Hash.StartsWith(commit, StringComparison.OrdinalIgnoreCase) || c.ShortHash == commit);
        string name = info is null ? commit : $"{info.ShortHash} ({info.Subject})";
        bool changed = editor.EditMap($"Go back to commit {name}", _ => old);
        Log.LogInformation("Went back to commit {Commit}", info?.ShortHash ?? commit);
        editor.Status = changed
            ? $"The map is as commit {name} holds it: undo comes back; save and commit to keep it"
            : $"The map is as commit {name} holds it already";
    });

    private string RequireFolder() => editor.Map?.ProjectFolder ?? throw new InvalidOperationException("Save the map as a project first");

    [RelayCommand]
    private Task Push() => OnPageAsync(PushAsync);

    [RelayCommand]
    private Task Pull() => OnPageAsync(PullAsync);

    [RelayCommand]
    private Task GoBack() => OnPageAsync(() => ChosenCommit is CommitInfo chosen ? RestoreAsync(chosen.Hash) : Task.CompletedTask);

    public string LfsNote => Repository is { LfsInstalled: false } && UseLfs
        ? "Git LFS is not installed on this computer: install it from git-lfs.com, or turn this off."
        : "Images, raw heightmaps and the World Machine and Gaea files go to Git LFS: every saved version stays in the history without making the repository heavy.";

    /// <summary>
    /// Opens the page for the open project: the setup when it is no repository yet, with the
    /// author git knows; otherwise the commit.
    /// </summary>
    public async Task OpenAsync()
    {
        if (editor.Map?.ProjectFolder is not string folder)
        {
            editor.Status = "Save the map as a project first: version control keeps the project's folder";
            return;
        }
        Error = null;
        RepositoryStatus status = await Task.Run(() => ProjectRepository.Status(folder));
        Repository = status;
        History = status.IsRepository ? await Task.Run(() => ProjectRepository.History(folder)) : [];
        // the same status still reads differently with the map's unsaved changes
        OnPropertyChanged(nameof(Summary));
        if (!status.GitInstalled)
        {
            Error = "Git is not installed on this computer: install it from git-scm.com.";
        }
        else if (!status.IsRepository && AuthorName.Length == 0 && AuthorEmail.Length == 0)
        {
            (AuthorName, AuthorEmail) = await Task.Run(() => ProjectRepository.DefaultAuthor(folder));
        }
        IsOpen = true;
    }

    /// <summary>
    /// Makes the project a repository with the page's choices and commits it as it is, saved first.
    /// </summary>
    public Task EnableAsync() => editor.BusyAsync("git-enable", "Turning on version control...", async () =>
    {
        EditedMap map = editor.Map ?? throw new InvalidOperationException("No map is open");
        string folder = map.ProjectFolder ?? throw new InvalidOperationException("Save the map as a project first");
        if (map.Session.IsModified)
        {
            await map.SaveAsync();
        }
        RepositorySettings settings = new RepositorySettings { Name = AuthorName, Email = AuthorEmail, Lfs = UseLfs, Remote = Remote };
        await Task.Run(() => ProjectRepository.Enable(folder, settings));
        await ReadAsync(folder);
        Log.LogInformation("Version control is on (LFS {Lfs}, shared {Shared})", settings.Lfs, settings.Remote.Length > 0);
        editor.Status = $"Version control is on: {Repository.LastCommit}";
    });

    /// <summary>
    /// Saves the map when it has changes, then commits everything that changed in the project.
    /// </summary>
    /// <param name="message">What changed; the page's <see cref="CommitMessage"/> when left out.</param>
    /// <returns>The commit's short hash, or <c>null</c> when nothing changed.</returns>
    public async Task<string?> CommitAsync(string? message = null)
    {
        string? commit = null;
        await editor.BusyAsync("commit", "Committing...", async () =>
        {
            EditedMap map = editor.Map ?? throw new InvalidOperationException("No map is open");
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
            await ReadAsync(folder);
            if (commit is not null)
            {
                CommitMessage = "";
                Log.LogInformation("Committed {Commit}", commit);
            }
            editor.Status = commit is null ? "Nothing changed since the last commit" : $"Committed {commit}: {text.Trim()}";
        });
        return commit;
    }

    [RelayCommand]
    private Task TurnOn() => OnPageAsync(async () =>
    {
        await EnableAsync();
        IsOpen = false;
    });

    [RelayCommand]
    private Task CommitChanges() => OnPageAsync(async () =>
    {
        if (await CommitAsync() is not null)
        {
            IsOpen = false;
        }
    });

    [RelayCommand]
    private void Close() => IsOpen = false;

    /// <summary>
    /// Runs an operation of the page, showing on it why it failed.
    /// </summary>
    private Task OnPageAsync(Func<Task> operation) => editor.TryAsync(async () =>
    {
        Error = null;
        try
        {
            await operation();
        }
        catch (Exception exception)
        {
            Error = exception.Message;
            throw;
        }
    });
}
