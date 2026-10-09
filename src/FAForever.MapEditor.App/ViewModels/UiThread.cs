namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// Work for the window's thread from another one (a file watcher, a logger on the pool).
/// </summary>
internal static class UiThread
{
    /// <summary>
    /// Runs <paramref name="action"/> at once on the window's thread, or posts it there.
    /// </summary>
    /// <param name="ui">The window's thread; null without a window (tests), and it runs at once.</param>
    public static void Run(SynchronizationContext? ui, Action action)
    {
        if (ui is null || SynchronizationContext.Current == ui)
        {
            action();
        }
        else
        {
            ui.Post(_ => action(), null);
        }
    }
}
