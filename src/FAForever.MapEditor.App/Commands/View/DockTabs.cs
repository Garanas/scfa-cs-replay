using FAForever.MapEditor.App.ViewModels;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Shows the Problems tab of the dock (user story F-14).
/// </summary>
[EditorCommand("view.problems", "Problems", Story = "F-14")]
public sealed record ShowProblemsTab : IRequest;

/// <summary>
/// Shows the History tab of the dock (user story F-4).
/// </summary>
[EditorCommand("view.history", "History", Story = "F-4")]
public sealed record ShowHistoryTab : IRequest;

/// <summary>
/// Shows the Assets tab of the dock (user story F-8).
/// </summary>
[EditorCommand("view.assets", "Assets", Story = "F-8")]
public sealed record ShowAssetsTab : IRequest;

/// <summary>
/// Shows the Console tab of the dock (user story F-17).
/// </summary>
[EditorCommand("view.console", "Console", Story = "F-17")]
public sealed record ShowConsoleTab : IRequest;

/// <summary>
/// Chooses a tab of the dock: 0 Problems, 1 History, 2 Assets, 3 Console.
/// </summary>
public sealed class DockTabHandlers(MainViewModel model) :
    IRequestHandler<ShowProblemsTab>, IRequestHandler<ShowHistoryTab>, IRequestHandler<ShowAssetsTab>, IRequestHandler<ShowConsoleTab>
{
    public Task Handle(ShowProblemsTab request, CancellationToken cancellationToken) => Show(0);

    public Task Handle(ShowHistoryTab request, CancellationToken cancellationToken) => Show(1);

    public Task Handle(ShowAssetsTab request, CancellationToken cancellationToken) => Show(2);

    public Task Handle(ShowConsoleTab request, CancellationToken cancellationToken) => Show(3);

    private Task Show(int tab)
    {
        model.DockIndex = tab;
        return Task.CompletedTask;
    }
}
