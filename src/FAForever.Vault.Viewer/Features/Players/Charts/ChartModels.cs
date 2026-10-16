namespace FAForever.Vault.Viewer.Features.Players.Charts;

/// <summary>One point of a <see cref="LineChart"/>. <paramref name="Data"/> is the caller's own record (a game), handed back to the tooltip.</summary>
public sealed record LinePoint(DateTimeOffset Time, double Value, object? Data = null);

/// <summary>A horizontal bar: the value, plus an optional second, paler part after it (lost games after won games).</summary>
/// <param name="CssColor">A CSS colour (<c>var(--swatch-uef)</c>) in place of <paramref name="Color"/>, for a faction's swatch.</param>
public sealed record BarRow(
    string Label,
    double Value,
    string ValueText,
    ChartColor Color = ChartColor.Primary,
    double Extra = 0,
    string? Sub = null,
    string? Href = null,
    string? CssColor = null);

/// <summary>A column; <paramref name="TopLabel"/> is the selective label above it, <paramref name="Label"/> the category below (empty to skip).</summary>
public sealed record ColumnItem(string Label, double Value, string? TopLabel = null, ChartColor Color = ChartColor.Primary, string? Title = null);

/// <summary>A bin of a <see cref="Histogram"/>: <paramref name="Sign"/> is -1 (a loss), 0 (the zero bin) or 1 (a win).</summary>
public sealed record HistogramBin(string Label, double Value, int Sign, string? TopLabel = null, string? Title = null);

/// <summary>A row of a <see cref="Dumbbell"/>: two values on one track.</summary>
public sealed record DumbbellRow(string Label, double A, double B, string AText, string BText, string? GapText = null);

/// <summary>An entry of a <see cref="Legend"/>.</summary>
public sealed record LegendItem(string Label, ChartColor Color = ChartColor.Primary, LegendKind Kind = LegendKind.Swatch, double Opacity = 1, string? CssColor = null);

public enum LegendKind
{
    Swatch,
    Line,
    Dot,
}

/// <summary>Whether a change is good, bad or neither; the sign says it in words, the mark's colour says it too.</summary>
public enum DeltaTone
{
    Neutral,
    Good,
    Bad,
}
