using Avalonia;
using Avalonia.Controls;

namespace FAForever.MapEditor.App.Tours;

/// <summary>
/// Where a step's target lies in the window, in the window's pixels.
/// </summary>
/// <param name="Found">Whether the target is there: a step without a target is found, its card in the middle.</param>
/// <param name="Bounds">The area to ring; null for a step without a target.</param>
/// <param name="Why">Why the target is not found or not reachable.</param>
/// <param name="Kind">What was named: <c>control</c>, <c>part</c>, <c>map</c> or <c>middle</c>.</param>
/// <param name="Control">The control found, which the overlay follows when the layout changes.</param>
public sealed record TourPlace(bool Found, Rect? Bounds, string? Why, string Kind, Control? Control = null)
{
    /// <summary>
    /// A step with nothing to point at.
    /// </summary>
    public static TourPlace Middle { get; } = new TourPlace(true, null, null, "middle");

    public static TourPlace Missing(string why, string kind) => new TourPlace(false, null, why, kind);
}
