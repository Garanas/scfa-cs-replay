using Avalonia;

namespace FAForever.MapEditor.App.Tours;

/// <summary>
/// Where a tour's card lies: beside the target, on the right if there is room, else the left, below or
/// above, and always inside the window. A target too large for any side (the views) gets the card over its
/// corner; a step without a target gets it in the middle.
/// </summary>
public static class TourCardPlacement
{
    /// <param name="window">The window's size.</param>
    /// <param name="target">The ringed area; null for a step without one.</param>
    /// <param name="card">The card's size.</param>
    /// <param name="gap">The distance between the card and the target.</param>
    /// <param name="edge">The least distance between the card and the window's edge.</param>
    public static Rect Place(Size window, Rect? target, Size card, double gap, double edge)
    {
        Rect inside = new Rect(edge, edge, Math.Max(0, window.Width - 2 * edge), Math.Max(0, window.Height - 2 * edge));
        Size size = new Size(Math.Min(card.Width, inside.Width), Math.Min(card.Height, inside.Height));
        Rect Clamp(double x, double y) => new Rect(
            Math.Clamp(x, inside.Left, Math.Max(inside.Left, inside.Right - size.Width)),
            Math.Clamp(y, inside.Top, Math.Max(inside.Top, inside.Bottom - size.Height)),
            size.Width, size.Height);
        if (target is not Rect area)
        {
            return Clamp((window.Width - size.Width) / 2, (window.Height - size.Height) / 2);
        }
        Rect[] sides =
        [
            // right, left: level with the target's middle
            Clamp(area.Right + gap, area.Center.Y - size.Height / 2),
            Clamp(area.Left - gap - size.Width, area.Center.Y - size.Height / 2),
            // below, above: in line with the target's middle
            Clamp(area.Center.X - size.Width / 2, area.Bottom + gap),
            Clamp(area.Center.X - size.Width / 2, area.Top - gap - size.Height),
        ];
        foreach (Rect side in sides)
        {
            if (!side.Intersects(area.Inflate(gap / 2)))
            {
                return side;
            }
        }
        // no side is free: over the target's corner, away from its middle
        return Clamp(area.Right - size.Width - gap, area.Bottom - size.Height - gap);
    }
}
