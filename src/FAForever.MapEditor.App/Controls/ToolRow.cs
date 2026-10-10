using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace FAForever.MapEditor.App.Controls;

/// <summary>
/// The active tool's options in one row of a fixed height, whatever the tool and the window's
/// width, so the views below never move: what does not fit goes behind a "More" button at the
/// row's end, which shows it in rows of the same height laid over the top of the views (and hides
/// it again with a second click, a click anywhere outside the row and what it shows, Escape, or
/// another tool). The settings keep their order: those at the end are the first to go.
/// </summary>
/// <remarks>
/// The overflowing settings stay where they are in the tree (their bindings do not change), laid
/// out below the row and clipped away while the row is closed. <see cref="IsOverflowHidden"/> tells
/// automation which ones a person cannot see now.
/// </remarks>
public sealed class ToolRow : Panel
{
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<ToolRow, double>(nameof(Spacing), 8);

    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<ToolRow, bool>(nameof(IsExpanded));

    /// <summary>
    /// The space below the row before the settings that overflow, and around them.
    /// </summary>
    public static readonly StyledProperty<Thickness> OverflowPaddingProperty =
        AvaloniaProperty.Register<ToolRow, Thickness>(nameof(OverflowPadding));

    private readonly ToggleButton _more = new ToggleButton();
    // behind the settings that do not fit, while they show (Border.tooloverflow)
    private readonly Border _box = new Border
    {
        IsVisible = false,
        // it fills the place it is given (the row's style centres its settings vertically)
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
    };
    private readonly HashSet<Control> _overflow = [];
    private TopLevel? _window;

    static ToolRow()
    {
        AffectsMeasure<ToolRow>(SpacingProperty, OverflowPaddingProperty);
        IsExpandedProperty.Changed.AddClassHandler<ToolRow>((row, _) => row.OnExpandedChanged());
    }

    public ToolRow()
    {
        _more.Classes.Add("toolmore");
        _more.Content = "More";
        _more.IsVisible = false;
        AutomationProperties.SetName(_more, "More options of the tool");
        AutomationProperties.SetHelpText(_more, "Shows the settings that do not fit in the row, over the views; a second click hides them");
        _more.IsCheckedChanged += (_, _) => IsExpanded = _more.IsChecked == true;
        _box.Classes.Add("tooloverflow");
        Children.Add(_box);
        Children.Add(_more);
        ClipToBounds = true;
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>
    /// Whether the settings that do not fit show, over the views.
    /// </summary>
    public bool IsExpanded
    {
        get => GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    public Thickness OverflowPadding
    {
        get => GetValue(OverflowPaddingProperty);
        set => SetValue(OverflowPaddingProperty, value);
    }

    /// <summary>
    /// The "More" button (shown only when something overflows).
    /// </summary>
    public ToggleButton MoreButton => _more;

    /// <summary>
    /// How many settings do not fit in the row.
    /// </summary>
    public int OverflowCount => _overflow.Count;

    /// <summary>
    /// Whether <paramref name="control"/> lies in a setting that does not fit in its tool row
    /// (whether or not the row shows it now).
    /// </summary>
    public static bool IsInOverflow(Control control) => RowOf(control) is (ToolRow row, Control item) && row._overflow.Contains(item);

    /// <summary>
    /// Whether <paramref name="control"/> lies in a setting that does not fit in its tool row while
    /// the row is closed: a person must click More to see it.
    /// </summary>
    public static bool IsOverflowHidden(Control control) => RowOf(control) is (ToolRow row, Control item) && !row.IsExpanded && row._overflow.Contains(item);

    /// <summary>
    /// Shows the settings that do not fit when <paramref name="control"/> is one of them, as a
    /// click on More would; true when it had to.
    /// </summary>
    public static bool Reveal(Control control)
    {
        if (RowOf(control) is (ToolRow row, Control item) && row._overflow.Contains(item) && !row.IsExpanded)
        {
            row.IsExpanded = true;
            return true;
        }
        return false;
    }

    private static (ToolRow Row, Control Item)? RowOf(Control control)
    {
        Visual? item = control;
        for (Visual? parent = control.GetVisualParent(); parent is not null; item = parent, parent = parent.GetVisualParent())
        {
            if (parent is ToolRow row)
            {
                return item is Control child ? (row, child) : null;
            }
        }
        return null;
    }

    private IEnumerable<Control> Items => Children.Where(child => child != _more && child != _box && child.IsVisible);

    protected override Size MeasureOverride(Size availableSize)
    {
        double height = double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height;
        Size free = new Size(double.PositiveInfinity, double.IsInfinity(availableSize.Height) ? double.PositiveInfinity : availableSize.Height);
        foreach (Control item in Items)
        {
            item.Measure(free);
            height = Math.Max(height, double.IsInfinity(availableSize.Height) ? item.DesiredSize.Height : 0);
        }
        _more.Measure(free);

        double width = availableSize.Width;
        double total = Items.Sum(item => item.DesiredSize.Width) + Spacing * Math.Max(0, Items.Count() - 1);
        bool overflows = !double.IsInfinity(width) && total > width;
        _overflow.Clear();
        if (overflows)
        {
            // keep what fits beside the More button, in order; the rest overflows
            double room = width - _more.DesiredSize.Width - Spacing;
            double x = 0;
            bool full = false;
            foreach (Control item in Items)
            {
                full |= x + item.DesiredSize.Width > room;
                if (full)
                {
                    _overflow.Add(item);
                }
                x += item.DesiredSize.Width + Spacing;
            }
        }
        if (_more.IsVisible != overflows)
        {
            _more.IsVisible = overflows;
        }
        if (!overflows && IsExpanded)
        {
            IsExpanded = false;
        }
        return new Size(double.IsInfinity(width) ? total : width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double height = finalSize.Height;
        double x = 0;
        foreach (Control item in Items.Where(item => !_overflow.Contains(item)))
        {
            item.Arrange(new Rect(x, 0, item.DesiredSize.Width, height));
            x += item.DesiredSize.Width + Spacing;
        }
        _more.Arrange(_more.IsVisible ? new Rect(finalSize.Width - _more.DesiredSize.Width, 0, _more.DesiredSize.Width, height) : default);

        // the settings that do not fit: rows of the same height below the row, over the views
        Thickness pad = OverflowPadding;
        double left = pad.Left, top = height + pad.Top, right = finalSize.Width - pad.Right;
        double rowX = left, rowY = top;
        foreach (Control item in Items.Where(_overflow.Contains))
        {
            if (rowX > left && rowX + item.DesiredSize.Width > right)
            {
                rowX = left;
                rowY += height;
            }
            item.Arrange(new Rect(rowX, rowY, item.DesiredSize.Width, height));
            rowX += item.DesiredSize.Width + Spacing;
        }
        // the box behind them spans the bar the row lies in (from the tool's name on)
        double boxLeft = 0, boxRight = finalSize.Width;
        if (this.FindAncestorOfType<Border>() is Border bar && this.TranslatePoint(default, bar) is Point at)
        {
            boxLeft = -at.X;
            boxRight = bar.Bounds.Width - at.X;
        }
        _box.Measure(Size.Infinity);
        _box.Arrange(_overflow.Count == 0 ? default : new Rect(boxLeft, height, boxRight - boxLeft, rowY + pad.Bottom));
        return finalSize;
    }

    private void OnExpandedChanged()
    {
        _more.IsChecked = IsExpanded;
        // closed, the overflowing settings lie below the row, clipped away (nor can the mouse reach them)
        ClipToBounds = !IsExpanded;
        _box.IsVisible = IsExpanded;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _window = TopLevel.GetTopLevel(this);
        // the window's presses and keys before anything else sees them: a click elsewhere or Escape closes
        _window?.AddHandler(PointerPressedEvent, OnWindowPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        _window?.AddHandler(KeyDownEvent, OnWindowKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _window?.RemoveHandler(PointerPressedEvent, OnWindowPressed);
        _window?.RemoveHandler(KeyDownEvent, OnWindowKeyDown);
        _window = null;
    }

    /// <summary>
    /// A press anywhere in the window while the settings that do not fit show: outside the row and
    /// them (and outside a list a combo box of theirs dropped down) it closes them, and the press
    /// goes on to what it reached.
    /// </summary>
    private void OnWindowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsExpanded || e.Source is not Visual source)
        {
            return;
        }
        if (source == this || this.IsVisualAncestorOf(source)
            || source.FindAncestorOfType<OverlayLayer>(includeSelf: true) is not null
            || source.FindAncestorOfType<PopupRoot>(includeSelf: true) is not null)
        {
            return;
        }
        IsExpanded = false;
    }

    /// <summary>
    /// Escape while the settings that do not fit show closes them, wherever the keyboard is; the
    /// key goes no further (a view's Escape would also clear the selection).
    /// </summary>
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && IsExpanded)
        {
            IsExpanded = false;
            e.Handled = true;
        }
    }
}
