using FAForever.MapEditor.App.ViewModels;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Shows or hides a panel of the window (user story V-4): <c>modePanel</c>, <c>inspector</c>,
/// <c>dock</c> or <c>toolbar</c>; without <paramref name="On"/> it turns it the other way.
/// </summary>
/// <returns>Whether the panel shows now.</returns>
[EditorCommand("view.togglePanel", "Show or hide a panel", Story = "V-4")]
public sealed record TogglePanel(string Panel, bool? On = null) : IRequest<bool>;

/// <summary>
/// Shows or hides the mode's panel left of the views (user story V-4).
/// </summary>
[EditorCommand("view.modePanel", "Mode panel", Story = "V-4")]
public sealed record ToggleModePanel(bool? On = null) : IRequest<bool>;

/// <summary>
/// Shows or hides the inspector right of the views (user story V-4).
/// </summary>
[EditorCommand("view.inspector", "Inspector", Story = "V-4")]
public sealed record ToggleInspector(bool? On = null) : IRequest<bool>;

/// <summary>
/// Shows or hides the dock under the views (user story V-4).
/// </summary>
[EditorCommand("view.dock", "Dock", Story = "V-4")]
public sealed record ToggleDock(bool? On = null) : IRequest<bool>;

/// <summary>
/// Shows or hides the toolbar of commands in the menu bar (user story F-1).
/// </summary>
[EditorCommand("view.toolbar", "Toolbar", Story = "F-1")]
public sealed record ToggleToolbar(bool? On = null) : IRequest<bool>;

/// <summary>
/// Gives a panel a size, as dragging its splitter does (user story V-4): the width of the mode's
/// panel or the inspector, the height of the dock, in device-independent pixels.
/// </summary>
[EditorCommand("view.resizePanel", "Resize a panel", Story = "V-4")]
public sealed record ResizePanel(string Panel, double Size) : IRequest;

/// <summary>
/// Shows every panel at its own size again, with the views split in half (user story V-4).
/// </summary>
[EditorCommand("view.resetLayout", "Reset layout", Story = "V-4")]
public sealed record ResetLayout : IRequest;

public sealed class LayoutHandlers(LayoutViewModel layout) :
    IRequestHandler<TogglePanel, bool>, IRequestHandler<ToggleModePanel, bool>, IRequestHandler<ToggleInspector, bool>,
    IRequestHandler<ToggleDock, bool>, IRequestHandler<ToggleToolbar, bool>, IRequestHandler<ResizePanel>, IRequestHandler<ResetLayout>
{
    public Task<bool> Handle(TogglePanel request, CancellationToken cancellationToken) => Task.FromResult(layout.Toggle(PanelOf(request.Panel), request.On));

    public Task<bool> Handle(ToggleModePanel request, CancellationToken cancellationToken) => Task.FromResult(layout.Toggle(LayoutPanel.ModePanel, request.On));

    public Task<bool> Handle(ToggleInspector request, CancellationToken cancellationToken) => Task.FromResult(layout.Toggle(LayoutPanel.Inspector, request.On));

    public Task<bool> Handle(ToggleDock request, CancellationToken cancellationToken) => Task.FromResult(layout.Toggle(LayoutPanel.Dock, request.On));

    public Task<bool> Handle(ToggleToolbar request, CancellationToken cancellationToken) => Task.FromResult(layout.Toggle(LayoutPanel.Toolbar, request.On));

    public Task Handle(ResizePanel request, CancellationToken cancellationToken)
    {
        layout.Resize(PanelOf(request.Panel), request.Size);
        return Task.CompletedTask;
    }

    public Task Handle(ResetLayout request, CancellationToken cancellationToken)
    {
        layout.Reset();
        return Task.CompletedTask;
    }

    /// <exception cref="ArgumentException">There is no such panel.</exception>
    public static LayoutPanel PanelOf(string name) => Enum.TryParse(name, ignoreCase: true, out LayoutPanel panel) && Enum.IsDefined(panel)
        ? panel
        : throw new ArgumentException($"There is no panel '{name}': modePanel, inspector, dock or toolbar");
}

/// <summary>
/// Puts these commands on the toolbar, in order (user story F-1); left out, the editor's own: undo,
/// redo, save, the command palette and the symmetry.
/// </summary>
/// <returns>The ids it left out because no command has them.</returns>
[EditorCommand("view.setToolbar", "Set the toolbar's commands", Story = "F-1")]
public sealed record SetToolbar(string[]? Commands = null) : IRequest<IReadOnlyList<string>>;

/// <summary>
/// Adds a command to the end of the toolbar (user story F-1).
/// </summary>
[EditorCommand("view.addToToolbar", "Add to the toolbar", Story = "F-1")]
public sealed record AddToToolbar(string Id) : IRequest;

/// <summary>
/// Takes a command off the toolbar (user story F-1), as its button's context menu does.
/// </summary>
[EditorCommand("view.removeFromToolbar", "Remove from the toolbar", Story = "F-1")]
public sealed record RemoveFromToolbar(string Id) : IRequest;

public sealed class ToolbarHandlers(LayoutViewModel layout) :
    IRequestHandler<SetToolbar, IReadOnlyList<string>>, IRequestHandler<AddToToolbar>, IRequestHandler<RemoveFromToolbar>
{
    public Task<IReadOnlyList<string>> Handle(SetToolbar request, CancellationToken cancellationToken) => Task.FromResult(layout.SetToolbar(request.Commands));

    public Task Handle(AddToToolbar request, CancellationToken cancellationToken)
    {
        layout.SetOnToolbar(request.Id, on: true);
        return Task.CompletedTask;
    }

    public Task Handle(RemoveFromToolbar request, CancellationToken cancellationToken)
    {
        layout.SetOnToolbar(request.Id, on: false);
        return Task.CompletedTask;
    }
}
