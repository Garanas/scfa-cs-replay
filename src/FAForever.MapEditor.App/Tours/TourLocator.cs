using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using FAForever.MapEditor.App.Automation;
using FAForever.MapEditor.App.Controls;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;

namespace FAForever.MapEditor.App.Tours;

/// <summary>
/// Finds what a step's target names, in the window, as automation's <c>click</c> does
/// (<see cref="UiLocator"/>): a control by its label, type and <c>under</c>, a region of the window, or a place
/// on the map. What a person must see to use it is shown first: a setting behind a tool row's More is revealed,
/// a panel that is hidden or floats in a window of its own is put back in its place (<see cref="TourLayoutLedger"/>
/// notes it, to put it back when the tour ends), a control is scrolled into view.
/// </summary>
public sealed class TourLocator(MainWindow window, MainViewModel model, TourLayoutLedger ledger)
{
    /// <summary>
    /// How long to wait for a frame of the window to be drawn: the mouse hits what the last frame drew.
    /// </summary>
    private const int FrameWaitMilliseconds = 250;

    private readonly UiLocator _locator = new UiLocator(window, model);

    /// <summary>
    /// The tour's overlay, which lies over the window (dimming and card): the check
    /// that the mouse reaches the target looks through it.
    /// </summary>
    public Visual? Overlay { get; set; }

    /// <summary>
    /// Where a target lies now, shown first; a target that cannot be shown says why.
    /// </summary>
    public async Task<TourPlace> ResolveAsync(TourTarget? target)
    {
        if (target is null)
        {
            return TourPlace.Middle;
        }
        try
        {
            if (target.Part is string part)
            {
                return await ResolvePartAsync(part);
            }
            if (target.Label is string)
            {
                return await ResolveLabelAsync(target);
            }
            if (target.Ref is not null || target.X is not null)
            {
                return await ResolveMapAsync(target);
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FormatException)
        {
            return TourPlace.Missing(exception.Message, target.Part is not null ? "part" : target.Label is not null ? "control" : "map");
        }
        return TourPlace.Middle;
    }

    /// <summary>
    /// A control's area in the window, cut to what scroll viewers around it show; null when it has no area.
    /// </summary>
    public Rect? BoundsOf(Control control)
    {
        if (control.TranslatePoint(default, window) is not Point origin)
        {
            return null;
        }
        Rect bounds = new Rect(origin, control.Bounds.Size);
        foreach (ScrollViewer viewer in control.GetVisualAncestors().OfType<ScrollViewer>())
        {
            if (viewer.TranslatePoint(default, window) is Point at)
            {
                bounds = bounds.Intersect(new Rect(at, viewer.Bounds.Size));
            }
        }
        return bounds.Intersect(new Rect(window.Bounds.Size)) is { Width: > 0, Height: > 0 } shown ? shown : null;
    }

    private async Task<TourPlace> ResolvePartAsync(string part)
    {
        MovablePanel? panel = part.ToLowerInvariant() switch
        {
            "panel" => MovablePanel.ModePanel,
            "inspector" => MovablePanel.Inspector,
            "dock" => MovablePanel.Dock,
            _ => null,
        };
        if (panel is MovablePanel needed && ledger.Ensure(needed))
        {
            await SettleAsync();
        }
        Control region = _locator.Region(part);
        await SettleAsync();
        return Place(region, "part", reach: false);
    }

    private async Task<TourPlace> ResolveLabelAsync(TourTarget target)
    {
        // what the step's commands changed (the mode's panel, a tool's options) is laid out after a frame
        await SettleAsync();
        IReadOnlyList<Control> matches = _locator.Matches(target.Label!, target.Type, target.Under);
        if (matches.Count == 0)
        {
            // in a panel the person hid, or outside the window in a floating one: show the panel, then look again
            Control? hidden = _locator.Matches(target.Label!, target.Type, target.Under, hidden: true).FirstOrDefault();
            if (hidden is not null && PanelOf(hidden) is MovablePanel panel && ledger.Ensure(panel))
            {
                await SettleAsync();
                matches = _locator.Matches(target.Label!, target.Type, target.Under);
            }
        }
        if (matches.Count == 0)
        {
            return TourPlace.Missing($"no control is named, labelled or reads '{target.Label}'" + (target.Type is null ? "" : $" ({target.Type})") + " in the window now", "control");
        }
        int index = target.Index ?? 0;
        if (index < 0 || index >= matches.Count)
        {
            return TourPlace.Missing($"'{target.Label}' fits {matches.Count} controls: index {index} is none of them (from 0)", "control");
        }
        Control control = matches[index];
        if (_locator.WindowOf(control) != window && PanelOf(control) is MovablePanel floating && ledger.Ensure(floating))
        {
            await SettleAsync();
            control = _locator.Matches(target.Label!, target.Type, target.Under).ElementAtOrDefault(index) ?? control;
        }
        ToolRow.Reveal(control);
        control.BringIntoView();
        await SettleAsync();
        TourPlace place = Place(control, "control", reach: true);
        for (int attempt = 0; attempt < 4 && place.Why is not null; attempt++)
        {
            // a list made again after the step's commands replaces its rows, and a panel just made is hit-tested some frames after its layout: look for the control again
            await Task.Delay(60);
            await SettleAsync();
            control = _locator.Matches(target.Label!, target.Type, target.Under).ElementAtOrDefault(index) ?? control;
            control.BringIntoView();
            await SettleAsync();
            place = Place(control, "control", reach: true);
        }
        return place;
    }

    private async Task<TourPlace> ResolveMapAsync(TourTarget target)
    {
        await SettleAsync();
        MapPlace place = _locator.PlaceOnMap(target.Ref, target.X, target.Z);
        (Control view, Point at, bool inside)[] views =
        [
            (window.MapView, place.Top, place.TopInside),
            (window.View3D, place.ThreeD ?? default, place.ThreeDInside),
        ];
        foreach ((Control view, Point at, bool inside) in views)
        {
            if (view.IsEffectivelyVisible && inside && view.TranslatePoint(at, window) is Point inWindow)
            {
                const double half = 20;
                return new TourPlace(true, new Rect(inWindow.X - half, inWindow.Y - half, 2 * half, 2 * half), null, "map");
            }
        }
        return TourPlace.Missing("the place is outside the views now", "map");
    }

    private TourPlace Place(Control control, string kind, bool reach)
    {
        if (_locator.WindowOf(control) != window)
        {
            return TourPlace.Missing("it lies in a window of its own", kind);
        }
        if (!control.IsEffectivelyVisible)
        {
            return TourPlace.Missing("it is hidden", kind);
        }
        if (BoundsOf(control) is not Rect bounds)
        {
            return TourPlace.Missing("it has no area in the window", kind);
        }
        string? why = reach ? UiLocator.WhyUnreachable(window, control, bounds.Center, Overlay) : null;
        if (why is not null && control is ItemsControl && ReachesAnItemOf(window, control))
        {
            // a list lets the mouse through between its rows: reached where one of them is
            why = null;
        }
        return new TourPlace(true, bounds, why, kind, control);
    }

    /// <summary>
    /// Whether the mouse reaches a button, a tree item or a list item inside a list at its centre.
    /// </summary>
    private bool ReachesAnItemOf(Window top, Control list) =>
        list.GetVisualDescendants().OfType<Control>().Any(item => item is Button or TreeViewItem or ListBoxItem
            && item.IsEffectivelyVisible && BoundsOf(item) is Rect bounds && UiLocator.WhyUnreachable(top, item, bounds.Center, Overlay) is null);

    /// <summary>
    /// The movable panel a control lies in: the mode's panel, the inspector, the dock, or the panel of a
    /// floating window.
    /// </summary>
    private static MovablePanel? PanelOf(Control control)
    {
        if (TopLevel.GetTopLevel(control) is FloatingPanelWindow floating)
        {
            return floating.Panel;
        }
        foreach (Visual ancestor in control.GetVisualAncestors())
        {
            switch (ancestor)
            {
                case ModePanel:
                    return MovablePanel.ModePanel;
                case Inspector:
                    return MovablePanel.Inspector;
                case Views.Dock:
                    return MovablePanel.Dock;
            }
        }
        return null;
    }

    /// <summary>
    /// Lays the window out and waits for a frame to be drawn, so that the mouse's hit testing and the places
    /// read agree with the layout.
    /// </summary>
    private async Task SettleAsync()
    {
        window.UpdateLayout();
        await UiLocator.NextFrameAsync(window, FrameWaitMilliseconds);
        window.UpdateLayout();
    }
}
