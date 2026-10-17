namespace FAForever.Vault.Viewer.Features.Players.Charts;

/// <summary>
/// The colours a chart mark can have: the theme's accent for the player's own series, the eight
/// chart colours (<c>viz-1</c> to <c>viz-8</c>, Styles/app.css) and the faint ink for what is only
/// context. Tailwind only sees class names that appear literally in a .razor or .cs file under the
/// Viewer, so every class a chart uses is spelled out here, never built from a number.
/// </summary>
public enum ChartColor
{
    Primary,
    Viz1,
    Viz2,
    Viz3,
    Viz4,
    Viz5,
    Viz6,
    Viz7,
    Viz8,
    Faint,
}

public static class ChartColors
{
    /// <summary>The background class of a bar, swatch or dot.</summary>
    public static string Bg(ChartColor color) => color switch
    {
        ChartColor.Primary => "bg-primary",
        ChartColor.Viz1 => "bg-viz-1",
        ChartColor.Viz2 => "bg-viz-2",
        ChartColor.Viz3 => "bg-viz-3",
        ChartColor.Viz4 => "bg-viz-4",
        ChartColor.Viz5 => "bg-viz-5",
        ChartColor.Viz6 => "bg-viz-6",
        ChartColor.Viz7 => "bg-viz-7",
        ChartColor.Viz8 => "bg-viz-8",
        _ => "bg-ink-faint",
    };

    /// <summary>The stroke class of a line in an SVG.</summary>
    public static string Stroke(ChartColor color) => color switch
    {
        ChartColor.Primary => "stroke-primary",
        ChartColor.Viz1 => "stroke-viz-1",
        ChartColor.Viz2 => "stroke-viz-2",
        ChartColor.Viz3 => "stroke-viz-3",
        ChartColor.Viz4 => "stroke-viz-4",
        ChartColor.Viz5 => "stroke-viz-5",
        ChartColor.Viz6 => "stroke-viz-6",
        ChartColor.Viz7 => "stroke-viz-7",
        ChartColor.Viz8 => "stroke-viz-8",
        _ => "stroke-ink-faint",
    };

    /// <summary>The fill class of a shape in an SVG.</summary>
    public static string Fill(ChartColor color) => color switch
    {
        ChartColor.Primary => "fill-primary",
        ChartColor.Viz1 => "fill-viz-1",
        ChartColor.Viz2 => "fill-viz-2",
        ChartColor.Viz3 => "fill-viz-3",
        ChartColor.Viz4 => "fill-viz-4",
        ChartColor.Viz5 => "fill-viz-5",
        ChartColor.Viz6 => "fill-viz-6",
        ChartColor.Viz7 => "fill-viz-7",
        ChartColor.Viz8 => "fill-viz-8",
        _ => "fill-ink-faint",
    };
}
