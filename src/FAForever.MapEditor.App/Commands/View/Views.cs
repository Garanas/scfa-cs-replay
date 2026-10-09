using FAForever.MapEditor.App.ViewModels;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Shows the map from above (user story V-1).
/// </summary>
[EditorCommand("view.top", "Top view", Gesture = "Ctrl+D1", Story = "V-1")]
public sealed record ShowTopView : IRequest;

public sealed class ShowTopViewHandler(MainViewModel model) : IRequestHandler<ShowTopView>
{
    public Task Handle(ShowTopView request, CancellationToken cancellationToken)
    {
        model.ViewIndex = 0;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Shows the map in 3D (user story V-2).
/// </summary>
[EditorCommand("view.3d", "3D view", Gesture = "Ctrl+D2", Story = "V-2")]
public sealed record Show3DView : IRequest;

public sealed class Show3DViewHandler(MainViewModel model) : IRequestHandler<Show3DView>
{
    public Task Handle(Show3DView request, CancellationToken cancellationToken)
    {
        model.ViewIndex = 1;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Shows the whole map in the top view.
/// </summary>
[EditorCommand("view.fit", "Fit the map", Gesture = "Ctrl+D0", Story = "V-1")]
public sealed record FitMap : IRequest;

public sealed class FitMapHandler(MainViewModel model) : IRequestHandler<FitMap>
{
    public Task Handle(FitMap request, CancellationToken cancellationToken)
    {
        model.RequestFit();
        return Task.CompletedTask;
    }
}

/// <summary>
/// Opens the command palette (user story F-2).
/// </summary>
[EditorCommand("view.palette", "Command palette", Gesture = "Ctrl+K", Story = "F-2")]
public sealed record OpenPalette : IRequest;

public sealed class OpenPaletteHandler(MainViewModel model) : IRequestHandler<OpenPalette>
{
    public Task Handle(OpenPalette request, CancellationToken cancellationToken)
    {
        model.OpenPalette();
        return Task.CompletedTask;
    }
}

[Incomplete("V-4", "The top and 3D views side by side")]
[EditorCommand("view.sideBySide", "Side by side", Gesture = "Ctrl+D3", Story = "V-4")]
public sealed record ShowSideBySide : IRequest;
