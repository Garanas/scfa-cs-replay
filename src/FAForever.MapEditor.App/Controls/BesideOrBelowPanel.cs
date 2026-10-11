using Avalonia;
using Avalonia.Controls;

namespace FAForever.MapEditor.App.Controls;

/// <summary>
/// Two children: the first, and a tag after it. The tag sits at the right of the first child on one
/// line when both fit (each at its natural size), else under it. A tag that is hidden takes no room.
/// For a tree row's "preset changed" next to its group's entry.
/// </summary>
public sealed class BesideOrBelowPanel : Panel
{
    public static readonly StyledProperty<double> GapProperty =
        AvaloniaProperty.Register<BesideOrBelowPanel, double>(nameof(Gap));

    /// <summary>
    /// The room between the two; set by the style from a token.
    /// </summary>
    public double Gap
    {
        get => GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    /// <summary>
    /// Whether the tag went under the first child at the last measure (for tests).
    /// </summary>
    public bool IsStacked { get; private set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        Control[] visible = [.. Children.Where(child => child.IsVisible)];
        if (visible.Length == 0)
        {
            return default;
        }
        Control main = visible[0];
        Control? tag = Children.Count > 1 && Children[1].IsVisible ? Children[1] : null;
        if (tag is null || main != Children[0])
        {
            IsStacked = false;
            main.Measure(availableSize);
            return main.DesiredSize;
        }
        main.Measure(Size.Infinity);
        tag.Measure(Size.Infinity);
        double oneLine = main.DesiredSize.Width + Gap + tag.DesiredSize.Width;
        IsStacked = oneLine > availableSize.Width;
        if (!IsStacked)
        {
            return new Size(availableSize.Width, Math.Max(main.DesiredSize.Height, tag.DesiredSize.Height));
        }
        main.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        tag.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        return new Size(availableSize.Width, main.DesiredSize.Height + tag.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Control main = Children[0];
        Control? tag = Children.Count > 1 && Children[1].IsVisible ? Children[1] : null;
        if (tag is null)
        {
            main.Arrange(new Rect(finalSize));
            return finalSize;
        }
        if (!IsStacked)
        {
            double tagWidth = Math.Min(tag.DesiredSize.Width, finalSize.Width);
            main.Arrange(new Rect(0, 0, Math.Max(0, finalSize.Width - tagWidth - Gap), finalSize.Height));
            tag.Arrange(new Rect(finalSize.Width - tagWidth, (finalSize.Height - tag.DesiredSize.Height) / 2, tagWidth, tag.DesiredSize.Height));
            return finalSize;
        }
        main.Arrange(new Rect(0, 0, finalSize.Width, main.DesiredSize.Height));
        tag.Arrange(new Rect(0, main.DesiredSize.Height, Math.Min(tag.DesiredSize.Width, finalSize.Width), tag.DesiredSize.Height));
        return finalSize;
    }
}
