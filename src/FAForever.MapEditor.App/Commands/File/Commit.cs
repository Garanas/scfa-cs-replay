using FAForever.MapEditor.App.ViewModels;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Commits the project (user story F-18): saves it when it has changes, then commits what changed.
/// </summary>
/// <param name="Message">What changed. Left out, the version control page opens: its setup while
/// the project is no repository, else the commit with its message.</param>
/// <returns>The commit's short hash; null when nothing changed or the page opened.</returns>
[EditorCommand("file.commit", "Commit...", Gesture = "Ctrl+Shift+C", Story = "F-18", Needs = Needs.Project | Needs.Idle)]
public sealed record Commit(string? Message = null) : IRequest<string?>;

public sealed class CommitHandler(VersionControlViewModel versionControl) : IRequestHandler<Commit, string?>
{
    public async Task<string?> Handle(Commit request, CancellationToken cancellationToken)
    {
        if (request.Message is null)
        {
            await versionControl.OpenAsync();
            return null;
        }
        return await versionControl.CommitAsync(request.Message);
    }
}
