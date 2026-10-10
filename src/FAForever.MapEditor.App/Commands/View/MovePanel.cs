using FAForever.MapEditor.App.ViewModels;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Moves a panel of the window (user story V-4): <c>modePanel</c>, <c>inspector</c>, <c>dock</c>, or
/// one of the dock's tabs (<c>problems</c>, <c>history</c>, <c>assets</c>, <c>console</c>), to the
/// <c>left</c>, the <c>right</c> or the <c>bottom</c>, into a window of its own (<c>float</c>), or a
/// tab back into the <c>dock</c>. A panel's context menu does the same.
/// </summary>
/// <returns>Where the panel lies now.</returns>
[EditorCommand("view.movePanel", "Move a panel", Story = "V-4")]
public sealed record MovePanel(string Panel, string Place) : IRequest<string>;

/// <summary>
/// Puts a floating panel back where it lay before it floated (user story V-4), as closing its
/// window does.
/// </summary>
/// <returns>Where the panel lies now.</returns>
[EditorCommand("view.returnPanel", "Put a floating panel back", Story = "V-4")]
public sealed record ReturnPanel(string Panel) : IRequest<string>;

public sealed class MovePanelHandlers(LayoutViewModel layout) : IRequestHandler<MovePanel, string>, IRequestHandler<ReturnPanel, string>
{
    public Task<string> Handle(MovePanel request, CancellationToken cancellationToken) =>
        Task.FromResult(layout.Move(LayoutViewModel.ParsePanel(request.Panel), LayoutViewModel.ParsePlace(request.Place)).ToString().ToLowerInvariant());

    public Task<string> Handle(ReturnPanel request, CancellationToken cancellationToken) =>
        Task.FromResult(layout.Return(LayoutViewModel.ParsePanel(request.Panel)).ToString().ToLowerInvariant());
}
