using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Shows or hides one kind of marker in both views (user story R-6): what the check boxes of the
/// Markers panel's list do. What is hidden is not picked either. The overlays Markers and Path nodes
/// still show or hide them all: a kind shown turns its overlay on, so it shows.
/// </summary>
/// <param name="Kind"><c>spawns</c>, <c>mass</c>, <c>hydrocarbon</c>, <c>ai</c>, <c>blank</c>, <c>weather</c>,
/// <c>landPaths</c>, <c>waterPaths</c>, <c>amphibiousPaths</c> or <c>airPaths</c>.</param>
/// <param name="On">Whether it shows; left out, it turns over (what the views show).</param>
/// <returns>Whether the views show it now.</returns>
[EditorCommand("markers.showKind", "Show a kind of marker", Story = "R-6")]
public sealed record ShowMarkerKind(string Kind, bool? On = null) : IRequest<bool>;

public sealed class ShowMarkerKindHandler(MainViewModel model, MarkersViewModel markers) : IRequestHandler<ShowMarkerKind, bool>
{
    public Task<bool> Handle(ShowMarkerKind request, CancellationToken cancellationToken)
    {
        MarkerKinds kind = MarkersViewModel.KindNamed(request.Kind);
        bool on = request.On ?? !markers.ShowsInViews(kind);
        markers.Show(kind, on);
        if (on && (kind & MarkerKinds.PathNodes) != 0)
        {
            model.ShowPathNodes = true;
        }
        if (on && (kind & ~MarkerKinds.PathNodes) != 0)
        {
            model.ShowMarkers = true;
        }
        return Task.FromResult(markers.ShowsInViews(kind));
    }
}
