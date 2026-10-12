using FAForever.MapEditor.App.ViewModels;

namespace FAForever.MapEditor.App.Tours;

/// <summary>
/// The layout a tour changed, so that it is put back when the tour ends: a panel the person hid or
/// floated in a window of its own is shown in its place for the step that points at it
/// (<see cref="Ensure"/>), and <see cref="Restore"/> hides or floats it again as it was.
/// </summary>
public sealed class TourLayoutLedger(LayoutViewModel layout)
{
    private readonly Dictionary<MovablePanel, (bool WasHidden, bool WasFloating)> _changed = [];

    /// <summary>
    /// Whether anything is put back when the tour ends.
    /// </summary>
    public bool Changed => _changed.Count > 0;

    /// <summary>
    /// The panels changed, for the state.
    /// </summary>
    public IEnumerable<string> Panels => _changed.Keys.Select(LayoutViewModel.Name);

    /// <summary>
    /// Whether a panel hides or floats now.
    /// </summary>
    public bool NeedsPlace(MovablePanel panel) => !layout.IsShown(panel) || layout.PlaceOf(panel) == PanelPlace.Float;

    /// <summary>
    /// Shows a panel in its own place: back from its window, and not hidden. Noted once, the first time, so
    /// <see cref="Restore"/> knows how the person had it.
    /// </summary>
    /// <returns>Whether the layout changed.</returns>
    public bool Ensure(MovablePanel panel)
    {
        bool hidden = !Shows(panel);
        bool floating = layout.PlaceOf(panel) == PanelPlace.Float;
        if (!hidden && !floating)
        {
            return false;
        }
        _changed.TryAdd(panel, (hidden, floating));
        if (floating)
        {
            layout.Return(panel);
        }
        if (hidden)
        {
            Show(panel, true);
        }
        return true;
    }

    /// <summary>
    /// Puts the panels back as the person had them.
    /// </summary>
    public void Restore()
    {
        foreach ((MovablePanel panel, (bool wasHidden, bool wasFloating)) in _changed.Reverse())
        {
            if (wasFloating)
            {
                layout.Move(panel, PanelPlace.Float);
            }
            if (wasHidden)
            {
                Show(panel, false);
            }
        }
        _changed.Clear();
    }

    private bool Shows(MovablePanel panel) => panel switch
    {
        MovablePanel.ModePanel => layout.ShowModePanel,
        MovablePanel.Inspector => layout.ShowInspector,
        MovablePanel.Dock => layout.ShowDock,
        _ => true,
    };

    private void Show(MovablePanel panel, bool on)
    {
        switch (panel)
        {
            case MovablePanel.ModePanel:
                layout.Toggle(LayoutPanel.ModePanel, on);
                break;
            case MovablePanel.Inspector:
                layout.Toggle(LayoutPanel.Inspector, on);
                break;
            case MovablePanel.Dock:
                layout.Toggle(LayoutPanel.Dock, on);
                break;
        }
    }
}
