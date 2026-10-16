using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Versioning;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Sends the project's commits to the repository chosen at the setup (user story F-18). Signing in
/// is the system's git credential helper's; the editor keeps no password.
/// </summary>
[EditorCommand("file.push", "Push", Story = "F-18", Needs = Needs.Project | Needs.Idle)]
public sealed record Push : IRequest;

/// <summary>
/// Takes the commits of the shared repository and reads what changed again (user story F-18);
/// refused while the map or project has changes that are not committed.
/// </summary>
/// <returns>The files that changed.</returns>
[EditorCommand("file.pull", "Pull", Story = "F-18", Needs = Needs.Project | Needs.Idle | Needs.NoStroke)]
public sealed record Pull : IRequest<IReadOnlyList<string>>;

/// <summary>
/// The project's commits, newest first (user story F-18); with a person, the version control page
/// opens on them.
/// </summary>
[EditorCommand("file.history", "History of commits...", Story = "F-18", Needs = Needs.Project | Needs.Idle)]
public sealed record ShowCommitHistory(int Count = 50) : IRequest<IReadOnlyList<CommitInfo>>;

/// <summary>
/// The files a commit changed; chooses it in the page's history (user story F-18).
/// </summary>
[EditorCommand("file.showCommit", "Show a commit", Story = "F-18", Needs = Needs.Project | Needs.Idle)]
public sealed record ShowCommit(string Commit) : IRequest<IReadOnlyList<CommitFile>>;

/// <summary>
/// Goes back to a commit (user story F-18): the map becomes the project as the commit holds it, as
/// one step of undo, unsaved; the files on disk and the history stay, so nothing is lost.
/// </summary>
[EditorCommand("file.restoreCommit", "Go back to a commit", Story = "F-18", Needs = Needs.Project | Needs.Idle | Needs.NoStroke)]
public sealed record RestoreCommit(string Commit) : IRequest;

public sealed class ShareHandlers(VersionControlViewModel versionControl) :
    IRequestHandler<Push>, IRequestHandler<Pull, IReadOnlyList<string>>, IRequestHandler<ShowCommitHistory, IReadOnlyList<CommitInfo>>,
    IRequestHandler<ShowCommit, IReadOnlyList<CommitFile>>, IRequestHandler<RestoreCommit>
{
    public Task Handle(Push request, CancellationToken cancellationToken) => versionControl.PushAsync();

    public Task<IReadOnlyList<string>> Handle(Pull request, CancellationToken cancellationToken) => versionControl.PullAsync();

    public async Task<IReadOnlyList<CommitInfo>> Handle(ShowCommitHistory request, CancellationToken cancellationToken)
    {
        await versionControl.OpenAsync();
        return [.. versionControl.History.Take(Math.Max(1, request.Count))];
    }

    public Task<IReadOnlyList<CommitFile>> Handle(ShowCommit request, CancellationToken cancellationToken) => versionControl.ShowCommitAsync(request.Commit);

    public Task Handle(RestoreCommit request, CancellationToken cancellationToken) => versionControl.RestoreAsync(request.Commit);
}
