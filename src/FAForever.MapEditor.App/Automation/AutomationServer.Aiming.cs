using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using FAForever.MapEditor.App.Controls;
using Point = Avalonia.Point;

namespace FAForever.MapEditor.App.Automation;

/// <summary>
/// Aiming automation's clicks at a control (<c>click</c>, and <c>set</c> on a check box, chip or
/// item): the control is shown first (behind the options row's More, scrolled into its scroll
/// viewer), then measured once its place stays the same and the mouse would reach it there. A
/// control the mouse cannot reach (covered, clipped, outside the window) is refused with why;
/// the click never goes elsewhere.
/// </summary>
public sealed partial class AutomationServer
{
    /// <summary>
    /// Frames <see cref="AimAsync"/> waits at most for the control's place to settle and the mouse to reach it.
    /// </summary>
    private const int AimFrames = 40;

    /// <summary>
    /// Where a click reaches <paramref name="control"/>, in its window's pixels: shown first, then
    /// measured once the layout after a scroll or a More opened is drawn and the window's hit
    /// testing (what the mouse uses: the last frame drawn) finds the control at that point.
    /// </summary>
    /// <param name="label">How the caller named it, for the reason it is refused.</param>
    /// <param name="at">The point in the control to aim at; its centre when null.</param>
    /// <returns>The window, the point and whether a More had to be opened.</returns>
    /// <exception cref="InvalidOperationException">The mouse cannot reach the control: why, and what it would hit.</exception>
    private async Task<(TopLevel Top, Point At, bool Revealed)> AimAsync(Control control, string label, Func<Control, Point>? at = null)
    {
        bool revealed = ToolRow.Reveal(control);
        if (!control.IsEffectivelyVisible)
        {
            throw new InvalidOperationException($"'{label}' is hidden (a collapsed part, a closed popup or another mode's)");
        }
        control.BringIntoView();
        TopLevel top = WindowOf(control);
        Point? last = null;
        Point point = default;
        string? why = null;
        int still = 0;
        for (int frame = 0; frame < AimFrames; frame++)
        {
            await LetInputSettleAsync();
            await NextFrameAsync(top);
            Point local = at?.Invoke(control) ?? new Point(control.Bounds.Width / 2, control.Bounds.Height / 2);
            point = control.TranslatePoint(local, top)
                ?? throw new InvalidOperationException($"'{label}' is not in the window");
            // the same place twice in a row, after a frame: the scroll and the layout after it are done
            still = last == point ? still + 1 : 0;
            last = point;
            if (still < 1)
            {
                if (frame == 0)
                {
                    // a scroll viewer asked to bring it into view once the first layout placed it
                    control.BringIntoView();
                }
                continue;
            }
            why = WhyUnreachable(top, control, point);
            if (why is null)
            {
                return (top, point, revealed);
            }
        }
        throw new InvalidOperationException($"'{label}' cannot be clicked at {point.X:0}, {point.Y:0}: {why ?? "it kept moving"}");
    }

    /// <summary>
    /// Why the mouse at <paramref name="point"/> would not reach <paramref name="control"/>, or
    /// null when it would: outside the window, or another control on top there (clipped by a
    /// scroll viewer, under the views, behind a panel).
    /// </summary>
    private static string? WhyUnreachable(TopLevel top, Control control, Point point)
    {
        if (!new Rect(top.Bounds.Size).Contains(point))
        {
            return "it lies outside the window";
        }
        if (!control.IsEffectivelyVisible)
        {
            return "it is hidden";
        }
        IInputElement? hit = top.InputHitTest(point);
        if (hit is Visual visual && (visual == control || control.IsVisualAncestorOf(visual)))
        {
            return null;
        }
        if (hit is Visual under && under.IsVisualAncestorOf(control)
            && (!control.IsHitTestVisible || ItemAround(control) is Control item && (item == under || item.IsVisualAncestorOf(under))))
        {
            // a control the mouse passes through (a text, a tree item's header text): reached when
            // nothing else lies over it, the item or button around it taking the click
            return null;
        }
        return hit is Visual other ? $"the mouse would reach {Describe(other)} there" : "the mouse would reach nothing there";
    }

    /// <summary>
    /// The popups open over the editor's windows (a combo box's list, a menu, the options row's More,
    /// a context menu): screenshots do not show them, yet a click elsewhere first closes them. Null
    /// when none is open (<c>key key=Escape</c> closes one).
    /// </summary>
    private JsonArray? PopupsState()
    {
        JsonArray open = [];
        foreach (Window each in Windows)
        {
            foreach (Control control in each.GetVisualDescendants().OfType<Control>())
            {
                string? what = control switch
                {
                    ComboBox { IsDropDownOpen: true } box => $"the list of {Describe(box)}",
                    MenuItem { IsSubMenuOpen: true } item => $"the menu {Describe(item)}",
                    ToolRow { IsExpanded: true } row => $"More of {Describe(row)}",
                    Avalonia.Controls.Primitives.Popup { IsOpen: true } popup when popup.TemplatedParent is not (ComboBox or MenuItem) => $"a popup in {Describe(popup.Parent as Visual ?? popup)}",
                    _ => null,
                };
                if (what is not null)
                {
                    open.Add(what);
                }
            }
            if (each.ContextMenu is { IsOpen: true })
            {
                open.Add("a context menu");
            }
        }
        return open.Count == 0 ? null : open;
    }

    /// <summary>
    /// The item of a tree or list, or the button, a control lies in: what a click on it reaches.
    /// </summary>
    private static Control? ItemAround(Control control) =>
        control.GetVisualAncestors().OfType<Control>().FirstOrDefault(each => each is TreeViewItem or ListBoxItem or Button);

    /// <summary>
    /// A control as a reason names it: its type and label or name, and the control it lies in.
    /// </summary>
    private static string Describe(Visual visual)
    {
        List<string> chain = [];
        for (Visual? each = visual; each is not null && chain.Count < 3; each = each.GetVisualParent())
        {
            if (each is Control control && (Avalonia.Automation.AutomationProperties.GetName(control) is { Length: > 0 } || control.Name is { Length: > 0 } || chain.Count == 0))
            {
                string name = Avalonia.Automation.AutomationProperties.GetName(control) is { Length: > 0 } label ? $" '{label}'" : control.Name is { Length: > 0 } named ? $" {named}" : "";
                chain.Add(control.GetType().Name + name);
            }
        }
        return string.Join(" in ", chain);
    }

    /// <summary>
    /// The next frame of a window drawn: the frame asked for and the one after it, so the scene
    /// the mouse hits holds the layout of the moment of the call.
    /// </summary>
    private async Task NextFrameAsync(TopLevel top)
    {
        for (int i = 0; i < 2; i++)
        {
            TaskCompletionSource drawn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            top.RequestAnimationFrame(_ => drawn.TrySetResult());
            await Task.WhenAny(drawn.Task, Task.Delay(1000));
        }
    }
}
