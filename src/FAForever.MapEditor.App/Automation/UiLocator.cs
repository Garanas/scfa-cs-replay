using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using FAForever.MapEditor.App.Controls;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using Point = Avalonia.Point;

namespace FAForever.MapEditor.App.Automation;

/// <summary>
/// Where things are in the window, for automation's <c>click</c>, <c>set</c>, <c>ui</c> and
/// <c>screenshot</c> and for the guided tours, so both agree on what a label means: the controls a
/// label, type and <c>under</c> name (<see cref="UiTree"/>), the named regions of the window
/// (<see cref="Region"/>), a place of the map in the views' pixels (<see cref="MapPlace"/>), and
/// whether the mouse reaches a control at a point (<see cref="WhyUnreachable"/>).
/// </summary>
public sealed class UiLocator(MainWindow window, MainViewModel model)
{
    /// <summary>
    /// The regions of the window <see cref="Region"/> takes, as <c>screenshot {part}</c> names them.
    /// </summary>
    public static readonly string[] Parts = ["window", "menu", "rail", "panel", "options", "views", "top", "3d", "dock", "inspector", "status", "dialog"];

    /// <summary>
    /// The editor's window, then the floating panels' windows.
    /// </summary>
    public IEnumerable<Window> Windows => [window, .. window.FloatingWindows];

    /// <summary>
    /// The first control named, labelled or reading the text, in the editor's window or a floating one.
    /// </summary>
    public Control? FindInWindows(string text, bool hidden = false) =>
        Windows.Select(each => UiTree.Find(each, text, hidden)).FirstOrDefault(control => control is not null);

    /// <summary>
    /// The first control or the part under a section heading named, labelled or reading the text, in
    /// the editor's window or a floating one.
    /// </summary>
    public Control? FindPartInWindows(string text, bool hidden = false) =>
        Windows.Select(each => UiTree.FindPart(each, text, hidden)).FirstOrDefault(control => control is not null);

    /// <summary>
    /// The part of the window <paramref name="under"/> names: that control or section, else null for
    /// every window.
    /// </summary>
    /// <exception cref="ArgumentException">Nothing is so named.</exception>
    public Visual? Under(string? under) => under is { Length: > 0 }
        ? FindPartInWindows(under) ?? throw new ArgumentException($"No control or heading is named, labelled or reads '{under}'")
        : null;

    /// <summary>
    /// Every control a label fits, the one <c>click</c> takes first (index 0): those named or labelled so,
    /// then those reading it that one can use, then texts; of a type (<c>Button</c> takes a toggle button
    /// too) and under a part of the window. Hidden controls too when <paramref name="hidden"/>.
    /// </summary>
    /// <param name="extraRoots">What else lies open over the windows (a menu's or a flyout's popup).</param>
    public IReadOnlyList<Control> Matches(string label, string? type = null, string? under = null, IEnumerable<Visual>? extraRoots = null, bool hidden = false)
    {
        Visual? part = Under(under);
        return [.. (part is not null ? [part] : Windows.Cast<Visual>().Concat(extraRoots ?? []))
            .SelectMany(root => UiTree.FindAll(root, label, hidden))
            .Distinct()
            .Where(each => type is null || UiTree.IsOfType(each, type))];
    }

    /// <summary>
    /// The window a control lies in: the editor's or a floating panel's.
    /// </summary>
    public TopLevel WindowOf(Visual control) => TopLevel.GetTopLevel(control) ?? window;

    /// <summary>
    /// A region of the window by its name (<see cref="Parts"/>).
    /// </summary>
    /// <exception cref="ArgumentException">There is no such region.</exception>
    /// <exception cref="InvalidOperationException">The window shows none now.</exception>
    public Control Region(string part) => part.ToLowerInvariant() switch
    {
        "window" => window,
        "menu" => window.MenuBar,
        "options" => Descendant<ToolOptions>(),
        "inspector" => Descendant<Inspector>(),
        "panel" => Descendant<ModePanel>(),
        "dock" => Descendant<Views.Dock>(),
        "rail" => Descendant<ModeRail>(),
        "views" => window.ViewsBar,
        "top" => window.MapView,
        "3d" => window.View3D,
        "status" => window.StatusBar,
        // the question over the window: its card, without the scrim over the rest
        "dialog" => model.Question.IsOpen
            ? Descendant<QuestionDialog>().GetVisualDescendants().OfType<Border>().FirstOrDefault(border => border.Name == "Dialog") ?? (Control)Descendant<QuestionDialog>()
            : throw new InvalidOperationException("No question shows over the window"),
        _ => throw new ArgumentException($"There is no part '{part}': {string.Join(", ", Parts)}"),
    };

    private T Descendant<T>() where T : Control =>
        Windows.SelectMany(each => each.GetVisualDescendants().OfType<T>()).FirstOrDefault(control => control.IsEffectivelyVisible)
        ?? throw new InvalidOperationException($"The window shows no {typeof(T).Name} now");

    /// <summary>
    /// Where a place of the map shows in the views, in each view's own pixels (<c>view-point</c>):
    /// the top view by its zoom and offset, the 3D view through the camera.
    /// </summary>
    /// <param name="reference">A thing of the map (<c>unit:UNIT_1</c>), at its own height; else <paramref name="x"/> and <paramref name="z"/>.</param>
    /// <param name="y">The height; the terrain's when left out.</param>
    /// <exception cref="InvalidOperationException">No map is open.</exception>
    /// <exception cref="ArgumentException">The map has no such thing, or no place is given.</exception>
    public MapPlace PlaceOnMap(string? reference, double? x, double? z, double? y = null)
    {
        MapDocument document = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
        Vector3 place;
        if (reference is not null)
        {
            ObjectRef thing = ObjectRef.Parse(reference);
            place = document.PlaceOf(thing) ?? throw new ArgumentException($"The map has no {thing}");
        }
        else
        {
            float px = (float)(x ?? throw new ArgumentException("x (or ref) is missing"));
            float pz = (float)(z ?? throw new ArgumentException("z is missing"));
            place = new Vector3(px, (float)(y ?? document.Terrain.Heightmap.HeightAt(px, pz)), pz);
        }
        Point top = window.MapView.MapToControl(place.X, place.Z);
        Point? threeD = AutomationServer.Project(model.Camera, window.View3D.Bounds.Size, place);
        return new MapPlace(place, top, new Rect(window.MapView.Bounds.Size).Contains(top), threeD, threeD is Point p && new Rect(window.View3D.Bounds.Size).Contains(p));
    }

    /// <summary>
    /// Why the mouse at <paramref name="point"/> would not reach <paramref name="control"/>, or null
    /// when it would: outside the window, or another control on top there (clipped by a scroll
    /// viewer, under the views, behind a panel).
    /// </summary>
    /// <param name="ignore">A layer that lies over everything, with what it holds, and lets the mouse through where the control is (the tour's dimming and card).</param>
    public static string? WhyUnreachable(TopLevel top, Control control, Point point, Visual? ignore = null)
    {
        if (!new Rect(top.Bounds.Size).Contains(point))
        {
            return "it lies outside the window";
        }
        if (!control.IsEffectivelyVisible)
        {
            return "it is hidden";
        }
        IInputElement? hit = ignore is null ? top.InputHitTest(point) : top.InputHitTest(point, visual => visual != ignore && !ignore.IsVisualAncestorOf(visual));
        if (hit is Visual visual && (visual == control || control.IsVisualAncestorOf(visual)))
        {
            return null;
        }
        if (hit is Visual under && under.IsVisualAncestorOf(control)
            && (!control.IsHitTestVisible || ItemAround(control) is Control item && (item == under || item.IsVisualAncestorOf(under))))
        {
            // a control the mouse passes through (a text, a tree item's header text): reached when
            // nothing else lies over it, the item or button around it taking the click
            return null;
        }
        return hit is Visual other ? $"the mouse would reach {Describe(other)} there" : "the mouse would reach nothing there";
    }

    /// <summary>
    /// The item of a tree or list, or the button, a control lies in: what a click on it reaches.
    /// </summary>
    public static Control? ItemAround(Control control) =>
        control.GetVisualAncestors().OfType<Control>().FirstOrDefault(each => each is TreeViewItem or ListBoxItem or Button);

    /// <summary>
    /// A control as a reason names it: its type and label or name, and the control it lies in.
    /// </summary>
    public static string Describe(Visual visual)
    {
        List<string> chain = [];
        for (Visual? each = visual; each is not null && chain.Count < 3; each = each.GetVisualParent())
        {
            if (each is Control control && (Avalonia.Automation.AutomationProperties.GetName(control) is { Length: > 0 } || control.Name is { Length: > 0 } || chain.Count == 0))
            {
                string name = Avalonia.Automation.AutomationProperties.GetName(control) is { Length: > 0 } label ? $" '{label}'" : control.Name is { Length: > 0 } named ? $" {named}" : "";
                chain.Add(control.GetType().Name + name);
            }
        }
        return string.Join(" in ", chain);
    }

    /// <summary>
    /// The next frame of a window drawn: the frame asked for and the one after it, so the scene
    /// the mouse hits holds the layout of the moment of the call.
    /// </summary>
    public static async Task NextFrameAsync(TopLevel top, int timeoutMilliseconds = 1000)
    {
        for (int i = 0; i < 2; i++)
        {
            TaskCompletionSource drawn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            top.RequestAnimationFrame(_ => drawn.TrySetResult());
            await Task.WhenAny(drawn.Task, Task.Delay(timeoutMilliseconds));
        }
    }

    /// <summary>
    /// Where a click on a control lands: its centre, but an item of a tree on its own line (the
    /// header's text), not in the middle of the items open below it.
    /// </summary>
    public static Point HeaderCentre(Control control)
    {
        if (control is TreeViewItem item
            && (item.GetVisualDescendants().OfType<Control>().FirstOrDefault(part => part.Name == "PART_HeaderPresenter")
                ?? item.GetVisualDescendants().OfType<Control>().FirstOrDefault(part => part.Name == "PART_Header")) is Control header
            && header.TranslatePoint(new Point(header.Bounds.Width / 2, header.Bounds.Height / 2), item) is Point inItem)
        {
            return inItem;
        }
        return new Point(control.Bounds.Width / 2, control.Bounds.Height / 2);
    }
}

/// <summary>
/// A place of the map in the views: its map position and its pixel in each view (the 3D one null behind the camera).
/// </summary>
public sealed record MapPlace(Vector3 Place, Point Top, bool TopInside, Point? ThreeD, bool ThreeDInside);
