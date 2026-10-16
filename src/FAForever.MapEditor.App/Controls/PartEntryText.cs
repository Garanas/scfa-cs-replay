using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace FAForever.MapEditor.App.Controls;

/// <summary>
/// The text of an entry of a mode panel's list, one look in every mode (Map, Markers, Units,
/// Water, Lighting, Procedural, Plugins, the map-wide decals): its title, and its detail (a count,
/// a state, a size) at the right of the title when both fit on one line, else under it, wrapping.
/// The title is never cut short by its detail. The entries of one list (the nearest
/// <see cref="ItemsControl"/> around them: a heading's entries, a level of a tree) go alike: when one
/// needs its detail under its title, every one has it there. A picture or swatch before it and a tag
/// after it belong to the entry's own template. Its look is in <c>Styles/Classes.axaml</c>
/// (<c>TextBlock.partentrytitle</c>, <c>TextBlock.partdetail.partentrydetail</c>).
/// </summary>
public sealed class PartEntryText : Panel
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<PartEntryText, string?>(nameof(Title));

    public static readonly StyledProperty<string?> DetailProperty =
        AvaloniaProperty.Register<PartEntryText, string?>(nameof(Detail));

    /// <summary>
    /// The room between the title and a detail beside it; set by the style from a token.
    /// </summary>
    public static readonly StyledProperty<double> GapProperty =
        AvaloniaProperty.Register<PartEntryText, double>(nameof(Gap));

    /// <summary>
    /// The detail says something is wrong or out of date (a result to generate again).
    /// </summary>
    public static readonly StyledProperty<bool> IsWarningProperty =
        AvaloniaProperty.Register<PartEntryText, bool>(nameof(IsWarning));

    /// <summary>
    /// Whether it goes alike with the other entries of its list (true); false puts its detail under
    /// its title only when it itself does not fit (a long list of presets).
    /// </summary>
    public static readonly StyledProperty<bool> IsAlikeProperty =
        AvaloniaProperty.Register<PartEntryText, bool>(nameof(IsAlike), true);

    public bool IsAlike
    {
        get => GetValue(IsAlikeProperty);
        set => SetValue(IsAlikeProperty, value);
    }

    private readonly TextBlock _title = new();
    private readonly TextBlock _detail = new();

    public PartEntryText()
    {
        _title.Classes.Add("partentrytitle");
        _detail.Classes.Add("partdetail");
        _detail.Classes.Add("partentrydetail");
        _detail.IsVisible = false;
        Children.Add(_title);
        Children.Add(_detail);
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Detail
    {
        get => GetValue(DetailProperty);
        set => SetValue(DetailProperty, value);
    }

    public double Gap
    {
        get => GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    public bool IsWarning
    {
        get => GetValue(IsWarningProperty);
        set => SetValue(IsWarningProperty, value);
    }

    /// <summary>
    /// Whether the detail went under the title at the last measure (for tests).
    /// </summary>
    public bool IsStacked { get; private set; }

    // the entries of the list this one is in, which go alike; null outside a list
    private PartEntryScope? _scope;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _scope = IsAlike && this.FindAncestorOfType<ItemsControl>() is ItemsControl list ? PartEntryScope.Of(list) : null;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _scope?.Leave(this);
        _scope = null;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TitleProperty)
        {
            _title.Text = Title;
        }
        else if (change.Property == DetailProperty)
        {
            _detail.Text = Detail;
            _detail.IsVisible = !string.IsNullOrEmpty(Detail);
        }
        else if (change.Property == IsWarningProperty)
        {
            _detail.Classes.Set("warning", IsWarning);
        }
        else if (change.Property == GapProperty)
        {
            InvalidateMeasure();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // the natural size of each on one line
        _title.Measure(Size.Infinity);
        Size title = _title.DesiredSize;
        if (!_detail.IsVisible)
        {
            IsStacked = false;
            _scope?.Report(this, fits: true);
            _title.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            return _title.DesiredSize;
        }
        _detail.Measure(Size.Infinity);
        Size detail = _detail.DesiredSize;
        double oneLine = title.Width + Gap + detail.Width;
        bool fits = oneLine <= availableSize.Width;
        IsStacked = _scope?.Report(this, fits) ?? !fits;
        if (!IsStacked)
        {
            return new Size(oneLine, Math.Max(title.Height, detail.Height));
        }
        Size room = new(availableSize.Width, double.PositiveInfinity);
        _title.Measure(room);
        _detail.Measure(room);
        return new Size(availableSize.Width, _title.DesiredSize.Height + _detail.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Size title = _title.DesiredSize;
        if (!_detail.IsVisible)
        {
            _title.Arrange(new Rect(0, (finalSize.Height - title.Height) / 2, finalSize.Width, title.Height));
            return finalSize;
        }
        Size detail = _detail.DesiredSize;
        if (!IsStacked)
        {
            double detailWidth = Math.Min(detail.Width, finalSize.Width);
            _title.Arrange(new Rect(0, (finalSize.Height - title.Height) / 2, Math.Max(0, finalSize.Width - detailWidth - Gap), title.Height));
            _detail.Arrange(new Rect(finalSize.Width - detailWidth, (finalSize.Height - detail.Height) / 2, detailWidth, detail.Height));
            return finalSize;
        }
        double top = Math.Max(0, (finalSize.Height - title.Height - detail.Height) / 2);
        _title.Arrange(new Rect(0, top, finalSize.Width, title.Height));
        _detail.Arrange(new Rect(0, top + title.Height, finalSize.Width, detail.Height));
        return finalSize;
    }
}

/// <summary>
/// The entries of one list, which put their details alike (<see cref="PartEntryText"/>): under their
/// titles when one of them does not fit on one line.
/// </summary>
internal sealed class PartEntryScope
{
    private static readonly ConditionalWeakTable<ItemsControl, PartEntryScope> Scopes = new();

    private readonly Dictionary<PartEntryText, bool> _fits = [];

    private bool _stacked;

    public static PartEntryScope Of(ItemsControl list) => Scopes.GetValue(list, _ => new PartEntryScope());

    /// <summary>
    /// Says whether an entry fits on one line; answers whether the list's entries go stacked. The
    /// others measure again when that changes.
    /// </summary>
    public bool Report(PartEntryText entry, bool fits)
    {
        _fits[entry] = fits;
        Update(entry);
        return _stacked;
    }

    public void Leave(PartEntryText entry)
    {
        if (_fits.Remove(entry))
        {
            Update(null);
        }
    }

    private void Update(PartEntryText? measuring)
    {
        bool stacked = _fits.ContainsValue(false);
        if (stacked == _stacked)
        {
            return;
        }
        _stacked = stacked;
        foreach (PartEntryText other in _fits.Keys)
        {
            if (other != measuring)
            {
                other.InvalidateMeasure();
            }
        }
    }
}
