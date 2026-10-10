using System.Text.Json.Nodes;
using FAForever.MapEditor.Project.Workspaces.Serialized;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The panels that can be moved (user story V-4): the mode's panel, the inspector, the dock and
/// each of the dock's tabs, which can lie on a side of their own.
/// </summary>
public enum MovablePanel
{
    ModePanel,
    Inspector,
    Dock,
    Problems,
    History,
    Assets,
    Console,
}

/// <summary>
/// Where a movable panel lies (user story V-4).
/// </summary>
public enum PanelPlace
{
    /// <summary>Left of the views, beside the rail of modes.</summary>
    Left,

    /// <summary>Right of the views.</summary>
    Right,

    /// <summary>Under the views.</summary>
    Bottom,

    /// <summary>In a window of its own, kept above the editor's.</summary>
    Float,

    /// <summary>A tab of the dock, where the dock lies (the tabs' own place).</summary>
    Dock,
}

/// <summary>
/// Where a floating panel's window is and how large: <paramref name="X"/> and <paramref name="Y"/>
/// on the screen in pixels (null for beside the editor's middle), the size in device-independent pixels.
/// </summary>
public readonly record struct FloatingBounds(int? X, int? Y, double Width, double Height);

/// <summary>
/// Where the panels lie (user story V-4): each on the left, the right, the bottom, floating in a
/// window of its own, or (a tab of the dock) in the dock. A side's size is the side's, whichever
/// panels lie there: the left's is <see cref="ModePanelWidth"/>, the right's
/// <see cref="InspectorWidth"/>, the bottom's <see cref="DockHeight"/> (named after the panel that
/// lies there by default). Panels on one side share it, one above the other (left and right) or
/// side by side (bottom), in the order of <see cref="MovablePanel"/>.
/// </summary>
public sealed partial class LayoutViewModel
{
    private readonly Dictionary<MovablePanel, PanelPlace> _places = [];
    private readonly Dictionary<MovablePanel, PanelPlace> _returns = [];
    private readonly Dictionary<MovablePanel, FloatingBounds> _floating = [];

    /// <summary>
    /// The size a floating window gets when it has none remembered.
    /// </summary>
    public static readonly FloatingBounds DefaultFloatingBounds = new FloatingBounds(null, null, 360, 520);

    /// <summary>
    /// Raised when a panel moves (or the layout is reset): the window puts the panels in their places.
    /// </summary>
    public event EventHandler? PlacesChanged;

    /// <summary>
    /// The place a panel lies in until it is moved.
    /// </summary>
    public static PanelPlace OwnPlace(MovablePanel panel) => panel switch
    {
        MovablePanel.ModePanel => PanelPlace.Left,
        MovablePanel.Inspector => PanelPlace.Right,
        MovablePanel.Dock => PanelPlace.Bottom,
        _ => PanelPlace.Dock,
    };

    /// <summary>
    /// Whether a panel is one of the dock's tabs.
    /// </summary>
    public static bool IsDockTab(MovablePanel panel) => panel >= MovablePanel.Problems;

    /// <summary>
    /// The dock's tab a panel is, for a tab.
    /// </summary>
    public static DockTab TabOf(MovablePanel panel) => (DockTab)(panel - MovablePanel.Problems);

    /// <summary>
    /// The panel a tab of the dock is.
    /// </summary>
    public static MovablePanel PanelOf(DockTab tab) => MovablePanel.Problems + (int)tab;

    /// <summary>
    /// What a panel is called in the window: its menu's and window's title.
    /// </summary>
    public static string TitleOf(MovablePanel panel) => panel switch
    {
        MovablePanel.ModePanel => "Mode panel",
        MovablePanel.Inspector => "Inspector",
        MovablePanel.Dock => "Dock",
        _ => TabOf(panel).ToString(),
    };

    public PanelPlace PlaceOf(MovablePanel panel) => _places.GetValueOrDefault(panel, OwnPlace(panel));

    /// <summary>
    /// Where a floating panel goes back to when its window closes: where it lay before it floated.
    /// </summary>
    public PanelPlace ReturnOf(MovablePanel panel) => _returns.GetValueOrDefault(panel, OwnPlace(panel));

    /// <summary>
    /// The place and size of a floating panel's window, as it was left.
    /// </summary>
    public FloatingBounds FloatingBoundsOf(MovablePanel panel) => _floating.GetValueOrDefault(panel, DefaultFloatingBounds);

    /// <summary>
    /// The tabs that lie in the dock, in their order.
    /// </summary>
    public IReadOnlyList<DockTab> TabsInDock => [.. Enum.GetValues<DockTab>().Where(tab => PlaceOf(PanelOf(tab)) == PanelPlace.Dock)];

    /// <summary>
    /// Whether a panel shows where it lies: the mode's panel, the inspector and the dock while they
    /// are not hidden (the dock while it holds a tab); a tab outside the dock always.
    /// </summary>
    public bool IsShown(MovablePanel panel) => panel switch
    {
        MovablePanel.ModePanel => ShowModePanel,
        MovablePanel.Inspector => ShowInspector,
        MovablePanel.Dock => ShowDock && TabsInDock.Count > 0,
        _ => PlaceOf(panel) != PanelPlace.Dock,
    };

    /// <summary>
    /// The panels that show on a side (left, right or bottom) or float, in their order.
    /// </summary>
    public IReadOnlyList<MovablePanel> PanelsAt(PanelPlace place) =>
        [.. Enum.GetValues<MovablePanel>().Where(panel => PlaceOf(panel) == place && IsShown(panel))];

    /// <summary>
    /// Moves a panel: to a side, into a window of its own (<see cref="PanelPlace.Float"/>), or a tab
    /// back into the dock. A floating panel remembers where it lay, to go back there when its
    /// window closes.
    /// </summary>
    /// <returns>Where it lies now.</returns>
    /// <exception cref="ArgumentException">The place is the dock and the panel no tab.</exception>
    public PanelPlace Move(MovablePanel panel, PanelPlace place)
    {
        if (place == PanelPlace.Dock && !IsDockTab(panel))
        {
            throw new ArgumentException($"Only the dock's tabs go into the dock; the {TitleOf(panel).ToLowerInvariant()} goes left, right, bottom or float");
        }
        PanelPlace now = PlaceOf(panel);
        if (now == place)
        {
            return place;
        }
        if (place == PanelPlace.Float)
        {
            _returns[panel] = now;
        }
        if (place == OwnPlace(panel))
        {
            _places.Remove(panel);
        }
        else
        {
            _places[panel] = place;
        }
        // a panel moved somewhere shows there
        switch (panel)
        {
            case MovablePanel.ModePanel:
                ShowModePanel = true;
                break;
            case MovablePanel.Inspector:
                ShowInspector = true;
                break;
            case MovablePanel.Dock:
                ShowDock = true;
                break;
        }
        Save();
        PlacesChanged?.Invoke(this, EventArgs.Empty);
        return place;
    }

    /// <summary>
    /// A floating panel's window closed: the panel goes back to where it lay before.
    /// </summary>
    /// <returns>Where it lies now.</returns>
    public PanelPlace Return(MovablePanel panel) => PlaceOf(panel) == PanelPlace.Float ? Move(panel, ReturnOf(panel)) : PlaceOf(panel);

    /// <summary>
    /// Remembers where a floating panel's window is and how large, as the person left it.
    /// </summary>
    public void SetFloatingBounds(MovablePanel panel, FloatingBounds bounds)
    {
        FloatingBounds kept = bounds with
        {
            Width = Math.Clamp(bounds.Width, MinimumPanelSize, 4 * MaximumPanelSize),
            Height = Math.Clamp(bounds.Height, MinimumPanelSize, 4 * MaximumPanelSize),
        };
        if (_floating.TryGetValue(panel, out FloatingBounds old) && old == kept)
        {
            return;
        }
        _floating[panel] = kept;
        Save();
    }

    /// <summary>
    /// The side a panel lies on when it is given a size: its splitter's.
    /// </summary>
    /// <exception cref="ArgumentException">It floats, or (a tab) lies in the dock.</exception>
    private PanelPlace SideOf(MovablePanel panel) => PlaceOf(panel) switch
    {
        PanelPlace.Float => throw new ArgumentException($"The {TitleOf(panel).ToLowerInvariant()} floats: its window has its own size"),
        PanelPlace.Dock => PanelPlace.Bottom,
        PanelPlace place => place,
    };

    /// <summary>
    /// Gives a side its size, as its splitter does.
    /// </summary>
    public void ResizeSide(PanelPlace side, double size)
    {
        double clamped = Math.Clamp(size, MinimumPanelSize, MaximumPanelSize);
        switch (side)
        {
            case PanelPlace.Left:
                ModePanelWidth = clamped;
                break;
            case PanelPlace.Right:
                InspectorWidth = clamped;
                break;
            case PanelPlace.Bottom or PanelPlace.Dock:
                DockHeight = clamped;
                break;
            default:
                throw new ArgumentException("A floating window has its own size");
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The panel named so (automation, the commands): <c>modePanel</c>, <c>inspector</c>, <c>dock</c>,
    /// <c>problems</c>, <c>history</c>, <c>assets</c> or <c>console</c>, ignoring case.
    /// </summary>
    /// <exception cref="ArgumentException">There is no such panel.</exception>
    public static MovablePanel ParsePanel(string name) => Enum.TryParse(name, ignoreCase: true, out MovablePanel panel) && Enum.IsDefined(panel)
        ? panel
        : throw new ArgumentException($"There is no panel '{name}' to move: {string.Join(", ", Enum.GetValues<MovablePanel>().Select(Name))}");

    /// <summary>
    /// The place named so: <c>left</c>, <c>right</c>, <c>bottom</c>, <c>float</c> or <c>dock</c>.
    /// </summary>
    /// <exception cref="ArgumentException">There is no such place.</exception>
    public static PanelPlace ParsePlace(string name) => Enum.TryParse(name, ignoreCase: true, out PanelPlace place) && Enum.IsDefined(place)
        ? place
        : throw new ArgumentException($"There is no place '{name}': left, right, bottom, float or dock (a tab)");

    /// <summary>
    /// A panel's name in the settings and automation: <c>modePanel</c>, <c>inspector</c>, ...
    /// </summary>
    public static string Name(MovablePanel panel) => char.ToLowerInvariant(panel.ToString()[0]) + panel.ToString()[1..];

    private static string Name(PanelPlace place) => place.ToString().ToLowerInvariant();

    /// <summary>
    /// Takes the places the settings remember; what they name that does not exist is left out.
    /// </summary>
    private void LoadPlaces(LayoutSettingsDto layout)
    {
        _places.Clear();
        _returns.Clear();
        _floating.Clear();
        foreach ((string key, string value) in layout.Places ?? [])
        {
            if (Enum.TryParse(key, ignoreCase: true, out MovablePanel panel) && Enum.TryParse(value, ignoreCase: true, out PanelPlace place)
                && (place != PanelPlace.Dock || IsDockTab(panel)) && place != OwnPlace(panel))
            {
                _places[panel] = place;
            }
            else
            {
                Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(_log, "The layout puts {Panel} in {Place}, which does not exist; it stays in its own place", key, value);
            }
        }
        foreach ((string key, FloatingPanelDto window) in layout.Floating ?? [])
        {
            if (!Enum.TryParse(key, ignoreCase: true, out MovablePanel panel))
            {
                continue;
            }
            if (window.Width is double width && window.Height is double height)
            {
                _floating[panel] = new FloatingBounds(window.X, window.Y, width, height);
            }
            if (window.Returns is string returns && Enum.TryParse(returns, ignoreCase: true, out PanelPlace back) && back != PanelPlace.Float
                && (back != PanelPlace.Dock || IsDockTab(panel)))
            {
                _returns[panel] = back;
            }
        }
    }

    /// <summary>
    /// The places to remember: only those that differ from the panels' own.
    /// </summary>
    private LayoutSettingsDto WithPlaces(LayoutSettingsDto layout)
    {
        Dictionary<string, FloatingPanelDto> floating = [];
        foreach (MovablePanel panel in _floating.Keys.Union(_returns.Keys))
        {
            FloatingBounds? bounds = _floating.TryGetValue(panel, out FloatingBounds b) ? b : null;
            floating[Name(panel)] = new FloatingPanelDto
            {
                X = bounds?.X,
                Y = bounds?.Y,
                Width = bounds?.Width,
                Height = bounds?.Height,
                Returns = _returns.TryGetValue(panel, out PanelPlace back) ? Name(back) : null,
            };
        }
        return layout with
        {
            Places = _places.Count == 0 ? null : _places.ToDictionary(pair => Name(pair.Key), pair => Name(pair.Value)),
            Floating = floating.Count == 0 ? null : floating,
        };
    }

    private void ResetPlaces()
    {
        _places.Clear();
        _returns.Clear();
        _floating.Clear();
    }

    /// <summary>
    /// What automation's <c>state.layout.places</c> shows: each panel's place, and its window's
    /// bounds while it floats.
    /// </summary>
    private JsonObject DescribePlaces() => new JsonObject([.. Enum.GetValues<MovablePanel>().Select(panel => KeyValuePair.Create(Name(panel), (JsonNode?)new JsonObject
    {
        ["place"] = Name(PlaceOf(panel)),
        ["shown"] = IsShown(panel),
        ["returns"] = PlaceOf(panel) == PanelPlace.Float ? Name(ReturnOf(panel)) : null,
    }))]);
}
