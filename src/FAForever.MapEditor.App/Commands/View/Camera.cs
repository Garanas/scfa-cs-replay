using FAForever.MapEditor.App.ViewModels;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Puts the 3D view's camera at once: what is left out stays (user story V-2).
/// </summary>
/// <param name="X">The place it looks at, in ogrids.</param>
/// <param name="Distance">From that place, in ogrids.</param>
/// <param name="Yaw">The turn around that place, in degrees.</param>
/// <param name="Pitch">The tilt from the horizon, in degrees: 90 looks straight down.</param>
[EditorCommand("camera.look", "Put the 3D camera", Story = "V-2")]
public sealed record LookCamera(float? X = null, float? Z = null, float? Distance = null, float? Yaw = null, float? Pitch = null) : IRequest;

public sealed class LookCameraHandler(MainViewModel model) : IRequestHandler<LookCamera>
{
    public Task Handle(LookCamera request, CancellationToken cancellationToken)
    {
        model.Camera.Look(request.X, request.Z, request.Distance, request.Yaw, request.Pitch);
        model.NotifyCameraMoved();
        return Task.CompletedTask;
    }
}

/// <summary>
/// Sends the 3D view's camera to a place of the map (user story V-1). With a radius it shows that
/// much around the place and glides there while the 3D view shows, as F does; without, it moves
/// there at once keeping its distance and angles, as control and a click in the top view do.
/// </summary>
/// <param name="Radius">How much around the place to show, in ogrids.</param>
[EditorCommand("camera.focus", "Send the 3D camera to a place", Story = "V-1")]
public sealed record FocusCamera(float X, float Z, float? Radius = null) : IRequest;

public sealed class FocusCameraHandler(MainViewModel model) : IRequestHandler<FocusCamera>
{
    public Task Handle(FocusCamera request, CancellationToken cancellationToken)
    {
        if (request.Radius is float radius)
        {
            model.FocusCamera(request.X, request.Z, radius);
        }
        else
        {
            model.MoveCameraTo(request.X, request.Z);
        }
        return Task.CompletedTask;
    }
}

/// <summary>
/// Shows the whole map in the 3D view, as Home does there (user story V-2).
/// </summary>
[EditorCommand("camera.home", "Show the whole map in 3D", Story = "V-2", Needs = Needs.Map)]
public sealed record ShowWholeMapIn3D : IRequest;

public sealed class ShowWholeMapIn3DHandler(MainViewModel model) : IRequestHandler<ShowWholeMapIn3D>
{
    public Task Handle(ShowWholeMapIn3D request, CancellationToken cancellationToken)
    {
        model.ResetCamera();
        return Task.CompletedTask;
    }
}
