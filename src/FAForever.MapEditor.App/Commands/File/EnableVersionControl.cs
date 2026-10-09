using FAForever.MapEditor.App.ViewModels;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Makes the project a git repository and commits it as it is (user story F-18), as the version
/// control page's button does. The choices left out keep what the page holds.
/// </summary>
/// <param name="Lfs">Whether images and the procedural sources go to Git LFS.</param>
/// <param name="Remote">The address of a repository to share the project through.</param>
[EditorCommand("file.enableVersionControl", "Turn on version control", Story = "F-18", Needs = Needs.Project | Needs.Idle)]
public sealed record EnableVersionControl(string? Name = null, string? Email = null, bool? Lfs = null, string? Remote = null) : IRequest;

public sealed class EnableVersionControlHandler(VersionControlViewModel versionControl) : IRequestHandler<EnableVersionControl>
{
    public async Task Handle(EnableVersionControl request, CancellationToken cancellationToken)
    {
        versionControl.AuthorName = request.Name ?? versionControl.AuthorName;
        versionControl.AuthorEmail = request.Email ?? versionControl.AuthorEmail;
        versionControl.UseLfs = request.Lfs ?? versionControl.UseLfs;
        versionControl.Remote = request.Remote ?? versionControl.Remote;
        await versionControl.EnableAsync();
    }
}
