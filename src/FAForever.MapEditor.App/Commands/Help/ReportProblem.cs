using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.ViewModels;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Bundles a problem report (user story F-17): the recent sessions' logs and metrics, the editor's
/// version, the operating system and, when chosen, the open map's project, into a zip in a folder,
/// shown in the file manager. Nothing is sent. Without a folder, the page opens to describe the
/// problem and choose.
/// </summary>
/// <param name="Folder">Where to write the zip.</param>
/// <param name="IncludeMap">Whether the open map's project goes in; off when left out.</param>
/// <param name="Sessions">How many of the newest sessions go in.</param>
/// <returns>The zip's path; null when the page opened.</returns>
[EditorCommand("help.reportProblem", "Report a problem...", Story = "F-17")]
public sealed record ReportProblem(string? Folder = null, string? Description = null, bool? IncludeMap = null, int Sessions = ProblemReport.DefaultSessions) : IRequest<string?>;

public sealed class ReportProblemHandler(ReportProblemViewModel report) : IRequestHandler<ReportProblem, string?>
{
    public async Task<string?> Handle(ReportProblem request, CancellationToken cancellationToken)
    {
        if (request.Folder is null)
        {
            report.Open();
            return null;
        }
        return await report.WriteAsync(request.Folder, request.Description ?? "", request.IncludeMap ?? false, request.Sessions);
    }
}
