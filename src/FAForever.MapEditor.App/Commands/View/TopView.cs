using FAForever.MapEditor.App.ViewModels;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Centres the top view on a place of the map (user story V-1).
/// </summary>
/// <param name="X">In ogrids.</param>
/// <param name="Zoom">Screen pixels per ogrid; left out, the zoom stays.</param>
[EditorCommand("view.zoomTo", "Centre the top view on a place", Story = "V-1")]
public sealed record ZoomTopView(double X, double Z, double? Zoom = null) : IRequest;

public sealed class ZoomTopViewHandler(MainViewModel model) : IRequestHandler<ZoomTopView>
{
    public Task Handle(ZoomTopView request, CancellationToken cancellationToken)
    {
        model.RequestTopView(request.X, request.Z, request.Zoom);
        return Task.CompletedTask;
    }
}

/// <summary>
/// What the 3D view's terrain shows, to find out why it looks wrong (user story F-16).
/// </summary>
/// <param name="Mode">0 the picture, 1 the albedo, 2 the light, 3 the normals, 4 the heights, 5 the
/// lower layer, 6 the first four masks after the remap, 7 the masks as stored.</param>
[EditorCommand("view.gpuDebug", "Show a part of the 3D view's picture", Story = "F-16")]
public sealed record ShowGpuDebug(int Mode) : IRequest;

public sealed class ShowGpuDebugHandler(MainViewModel model) : IRequestHandler<ShowGpuDebug>
{
    public Task Handle(ShowGpuDebug request, CancellationToken cancellationToken)
    {
        model.GpuDebug = request.Mode is >= 0 and <= 7
            ? request.Mode
            : throw new ArgumentException($"There is no debug mode {request.Mode}: 0 to 7");
        return Task.CompletedTask;
    }
}
