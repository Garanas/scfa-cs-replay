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
            why = UiLocator.WhyUnreachable(top, control, point);
            if (why is null)
            {
                return (top, point, revealed);
            }
        }
        throw new InvalidOperationException($"'{label}' cannot be clicked at {point.X:0}, {point.Y:0}: {why ?? "it kept moving"}");
    }

    /// <summary>
    /// A control as a reason names it (<see cref="UiLocator.Describe"/>).
    /// </summary>
    private static string Describe(Visual visual) => UiLocator.Describe(visual);

    /// <summary>
    /// The next frame of a window drawn (<see cref="UiLocator.NextFrameAsync"/>).
    /// </summary>
    private static Task NextFrameAsync(TopLevel top) => UiLocator.NextFrameAsync(top);
}
