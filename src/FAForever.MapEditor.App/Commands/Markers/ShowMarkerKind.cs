using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Shows or hides one kind of marker in both views (user story R-6): what the check boxes of the
/// Markers panel's "By kind" do. What is hidden is not picked either. The overlays Markers and
/// Path nodes still show or hide them all.
/// </summary>
/// <param name="Kind"><c>spawns</c>, <c>mass</c>, <c>hydrocarbon</c>, <c>ai</c>, <c>weather</c>,
/// <c>landPaths</c>, <c>waterPaths</c>, <c>amphibiousPaths</c> or <c>airPaths</c>.</param>
/// <param name="On">Whether it shows; left out, it turns over.</param>
/// <returns>Whether it shows now.</returns>
[EditorCommand("markers.showKind", "Show a kind of marker", Story = "R-6")]
public sealed record ShowMarkerKind(string Kind, bool? On = null) : IRequest<bool>;

public sealed class ShowMarkerKindHandler(MarkersViewModel markers) : IRequestHandler<ShowMarkerKind, bool>
{
    public Task<bool> Handle(ShowMarkerKind request, CancellationToken cancellationToken)
    {
        MarkerKinds kind = MarkersViewModel.KindNamed(request.Kind);
        markers.Show(kind, request.On ?? !markers.Shows(kind));
        return Task.FromResult(markers.Shows(kind));
    }
}
