using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using FAForever.MapEditor.App.Controls;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.Tours;
using FAForever.MapEditor.App.ViewModels;

namespace FAForever.MapEditor.App.Views;

/// <summary>
/// The guided tour over the window (user story H-1): it finds the step's target (<see cref="TourLocator"/>), cuts the
/// hole and rings it, and places the card beside it (<see cref="TourCardPlacement"/>). It follows the target when the
/// layout changes: the target's own layout pass and the window's size, the layout's panels, the view, mode and tool
/// raise it again; nothing runs per frame.
/// </summary>
public sealed partial class TourOverlay : UserControl
{
    private MainViewModel? _model;
    private TourLocator? _locator;
    private TourPlace _place = TourPlace.Middle;
    private Control? _tracked;
    private Window? _window;
    private int _version;
    private Task _latest = Task.CompletedTask;
    private bool _refreshPosted;

    public TourOverlay()
    {
        InitializeComponent();
    }

    /// <summary>
    /// The dimming and the ring, for tests.
    /// </summary>
    public TourSpotlight Dimming => Spotlight;

    /// <summary>
    /// The step's card, for tests.
    /// </summary>
    public Border StepCard => Card;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_model is not null)
        {
            _model.Tour.Stage = null;
            _model.PropertyChanged -= OnModelChanged;
            _model.Layout.Changed -= OnLayoutChanged;
            _model.Layout.PlacesChanged -= OnLayoutChanged;
            _model.Tour.PropertyChanged -= OnTourChanged;
        }
        _model = DataContext as MainViewModel;
        if (_model is not null)
        {
            _model.Tour.Stage = ShowAsync;
            _model.PropertyChanged += OnModelChanged;
            _model.Layout.Changed += OnLayoutChanged;
            _model.Layout.PlacesChanged += OnLayoutChanged;
            _model.Tour.PropertyChanged += OnTourChanged;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window is not null)
        {
            _window.SizeChanged += OnWindowSized;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_window is not null)
        {
            _window.SizeChanged -= OnWindowSized;
        }
        Track(null);
    }

    private void OnWindowSized(object? sender, SizeChangedEventArgs e) => Refresh();

    private void OnLayoutChanged(object? sender, EventArgs e) => Refresh();

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.View) or nameof(MainViewModel.SelectedMode) or nameof(MainViewModel.ActiveTool))
        {
            Refresh();
        }
    }

    /// <summary>
    /// The card's text changed: its size, so its place, with it.
    /// </summary>
    private void OnTourChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TourViewModel.ShowsDone) or nameof(TourViewModel.HasProblem) or nameof(TourViewModel.IsWaiting))
        {
            Refresh();
        }
    }

    /// <summary>
    /// Finds the target again and draws the step, once the layout after what changed is done.
    /// </summary>
    private void Refresh()
    {
        if (_refreshPosted || _model?.Tour.IsRunning != true)
        {
            return;
        }
        _refreshPosted = true;
        Dispatcher.UIThread.Post(() =>
        {
            _refreshPosted = false;
            _ = ShowAsync();
        }, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Finds the step's target and draws the step: the hole, the ring and the card. What the tour's commands wait for.
    /// </summary>
    private Task ShowAsync() => _latest = ShowCoreAsync(++_version);

    private async Task ShowCoreAsync(int version)
    {
        if (_model is not MainViewModel model || TopLevel.GetTopLevel(this) is not MainWindow window)
        {
            return;
        }
        TourViewModel tour = model.Tour;
        if (!tour.IsRunning)
        {
            Track(null);
            _locator?.Collapse();
            Spotlight.Hole = null;
            Spotlight.LetThrough = false;
            return;
        }
        _locator ??= new TourLocator(window, model, tour.Ledger) { Overlay = this };
        using Performance.Span measure = Performance.Measure("tour.show");
        TourPlace place = await _locator.ResolveAsync(tour.Step?.Target);
        if (version != _version || !tour.IsRunning)
        {
            // a newer look at the step is on its way: whoever waited for this one waits for that
            if (version != _version && _latest is { } newer)
            {
                await newer;
            }
            return;
        }
        Apply(window, tour, place);
        Track(place.Control);
    }

    /// <summary>
    /// Cuts the hole, rings it and places the card.
    /// </summary>
    private void Apply(MainWindow window, TourViewModel tour, TourPlace place)
    {
        _place = place;
        Point origin = this.TranslatePoint(default, window) ?? default;
        Rect? hole = place.Bounds is Rect bounds ? bounds.Translate(-origin) : null;
        Spotlight.Hole = hole;
        Spotlight.LetThrough = tour.Step?.Acts == true && place.Found;
        Rect? card = null;
        if (tour.ShowCard)
        {
            Card.InvalidateMeasure();
            Card.Measure(new Size(Token("TourCardWidth", 320), Math.Max(0, Bounds.Height)));
            Rect? ringed = Spotlight.Ringed;
            card = TourCardPlacement.Place(Bounds.Size, ringed, Card.DesiredSize, Token("TourCardGap", 14), Token("TourCardEdge", 12));
            Card.Margin = new Thickness(card.Value.X, card.Value.Y, 0, 0);
        }
        tour.SetPlace(place, card is Rect inOverlay ? inOverlay.Translate(origin) : null);
    }

    /// <summary>
    /// Follows a control: when a layout pass moves or hides it, the hole follows.
    /// </summary>
    private void Track(Control? control)
    {
        if (_tracked == control)
        {
            return;
        }
        if (_tracked is not null)
        {
            _tracked.LayoutUpdated -= OnTrackedLayout;
        }
        _tracked = control;
        if (_tracked is not null)
        {
            _tracked.LayoutUpdated += OnTrackedLayout;
        }
    }

    private void OnTrackedLayout(object? sender, EventArgs e)
    {
        if (_tracked is not Control control || _locator is null || _model is not MainViewModel model || TopLevel.GetTopLevel(this) is not MainWindow window || !model.Tour.IsRunning)
        {
            return;
        }
        if (!control.IsEffectivelyVisible || _locator.BoundsOf(control) is not Rect now)
        {
            // gone: another view, mode or panel; find it again
            Refresh();
            return;
        }
        if (_place.Bounds != now)
        {
            Apply(window, model.Tour, _place with { Bounds = now });
        }
    }

    private double Token(string key, double fallback) => this.TryFindResource(key, ActualThemeVariant, out object? value) && value is double token ? token : fallback;
}
