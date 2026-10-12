using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering;

namespace FAForever.MapEditor.App.Controls;

/// <summary>
/// The guided tour's dimming (user story H-1): a scrim over the whole window with a hole at the step's
/// target, and a ring around the hole. The scrim takes the mouse, so the window cannot be used while the
/// tour explains it; at a step that asks the person to act (<see cref="LetThrough"/>) the hole lets the
/// mouse through to the target. Keys are not taken: the window's shortcuts keep working.
/// </summary>
/// <remarks>
/// Drawn when it changes, never per frame; the colours and sizes are tokens (<c>TourScrimBrush</c>,
/// <c>TourRingBrush</c>, <c>TourRingThickness</c>, <c>TourHolePadding</c>, <c>TourHoleRadius</c>), read again when
/// the theme variant changes.
/// </remarks>
public sealed class TourSpotlight : Control, ICustomHitTest
{
    /// <summary>
    /// The target's area in this control's pixels; null for a step without a target (the whole window dims).
    /// </summary>
    public static readonly StyledProperty<Rect?> HoleProperty = AvaloniaProperty.Register<TourSpotlight, Rect?>(nameof(Hole));

    /// <summary>
    /// Whether the hole lets the mouse through.
    /// </summary>
    public static readonly StyledProperty<bool> LetThroughProperty = AvaloniaProperty.Register<TourSpotlight, bool>(nameof(LetThrough));

    private IBrush _scrim = Brushes.Transparent;
    private IBrush _ring = Brushes.Transparent;
    private double _ringThickness = 2;
    private double _padding = 4;
    private double _radius = 6;

    static TourSpotlight()
    {
        AffectsRender<TourSpotlight>(HoleProperty);
        AffectsMeasure<TourSpotlight>(HoleProperty);
    }

    public Rect? Hole
    {
        get => GetValue(HoleProperty);
        set => SetValue(HoleProperty, value);
    }

    public bool LetThrough
    {
        get => GetValue(LetThroughProperty);
        set => SetValue(LetThroughProperty, value);
    }

    /// <summary>
    /// The hole with its padding: what the ring surrounds.
    /// </summary>
    public Rect? Ringed => Hole?.Inflate(_padding).Intersect(new Rect(Bounds.Size));

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ActualThemeVariantChanged -= OnThemeChanged;
        ActualThemeVariantChanged += OnThemeChanged;
        ReadTokens();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        ActualThemeVariantChanged -= OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        ReadTokens();
        InvalidateVisual();
    }

    private void ReadTokens()
    {
        _scrim = Token("TourScrimBrush", _scrim);
        _ring = Token("TourRingBrush", _ring);
        _ringThickness = Token("TourRingThickness", _ringThickness);
        _padding = Token("TourHolePadding", _padding);
        _radius = Token("TourHoleRadius", _radius);
    }

    private T Token<T>(string key, T fallback) => this.TryFindResource(key, ActualThemeVariant, out object? value) && value is T token ? token : fallback;

    /// <summary>
    /// The scrim takes the mouse everywhere but through the hole of a step that asks the person to act.
    /// </summary>
    public bool HitTest(Point point) => !(LetThrough && Ringed is Rect hole && hole.Contains(point));

    public override void Render(DrawingContext context)
    {
        Rect all = new Rect(Bounds.Size);
        if (Ringed is not Rect hole || hole.Width <= 0 || hole.Height <= 0)
        {
            context.DrawRectangle(_scrim, null, all);
            return;
        }
        Geometry dimmed = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(all), new RectangleGeometry(hole, _radius, _radius));
        context.DrawGeometry(_scrim, null, dimmed);
        context.DrawRectangle(null, new Pen(_ring, _ringThickness), hole.Deflate(_ringThickness / 2), _radius, _radius);
    }
}
