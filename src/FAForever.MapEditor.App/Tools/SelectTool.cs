using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tools;

/// <summary>
/// Selects things on the map and moves them (user story F-11). A click selects the nearest thing
/// within a few pixels, shift adds it and control takes it out or adds it; a click on nothing
/// selects nothing. Dragging on empty ground draws a box that selects what lies in it (with shift
/// and control as a click): on the map in the top view, on screen in the 3D view, where what shows
/// inside the rectangle is selected. Dragging a selected thing moves the whole selection, one step of undo,
/// keeping each thing on the terrain. Its keys in a view: Escape selects nothing, Q and E turn the
/// selection by 15 degrees (with shift, or when only structures are selected, 90), the arrows move
/// it an ogrid (with shift 8). Holding R while dragging turns the selection as the mouse goes round
/// its middle.
/// </summary>
/// <remarks>
/// The selection has handles in both views (<see cref="Shapes"/>, laid out by the library's
/// <see cref="HandleLayout"/>): the square in the middle moves it, the arrows move it along x or z,
/// the ring turns it, the squares on its box scale what scales; a press takes a handle before
/// anything under it. Shift snaps a move to the grid of <see cref="HandleSettings.Snap"/>, a turn to
/// 15 degrees and a scale to tenths; holding X or Z moves along that axis. Turns and scales go
/// about the selection's centre or each thing's own (<see cref="HandleSettings.Pivot"/>). Every
/// drag is one step of undo, and every change goes through the library's <see cref="ObjectEdits"/>.
/// </remarks>
/// <param name="kinds">What it picks (<see cref="Kinds"/>).</param>
public sealed partial class SelectTool(MainViewModel model, ObjectKinds kinds) : ObservableObject, IMapTool
{
    /// <summary>
    /// How far, in pixels, the mouse moves before a press becomes a drag.
    /// </summary>
    private const float DragPixels = 3;

    private enum Gesture
    {
        None,
        Click,
        Move,
        Box,
        Handle,

        /// <summary>
        /// R held and the mouse dragged: the selection turns as the mouse goes round its middle.
        /// </summary>
        Turn,
    }

    private Gesture _gesture;
    private ToolInput _input = ToolInput.Default;
    private (float X, float Z) _from;
    private bool _dragging;
    private EditTransaction? _move;
    private MapDocument? _before;
    private ImmutableArray<ObjectRef> _moving = [];
    private ImmutableArray<SymmetryPartner> _partners = [];
    private HandlePart _part;
    private SelectionFrame? _frame;
    private Vector2 _pressPlane;
    private ToolView? _view;
    private ObjectRef? _clickUnder;
    private (MapDocument Document, ImmutableArray<ObjectRef> Items, SelectionFrame? Frame)? _frameOf;
    private Vector2? _screenFrom;
    private ToolView? _screenView;

    /// <summary>
    /// The kinds it picks, of those the views show: the Markers mode's tool markers and areas, the
    /// Units mode's units. A tool that selects everything takes <see cref="ObjectKinds.All"/>.
    /// </summary>
    [ObservableProperty]
    public partial ObjectKinds Kinds { get; set; } = kinds;

    /// <summary>
    /// The box being drawn in the top view, while it is.
    /// </summary>
    [ObservableProperty]
    public partial (float X0, float Z0, float X1, float Z1)? Box { get; private set; }

    /// <summary>
    /// The box being drawn in the 3D view, while it is: a rectangle of the view's pixels (corners in
    /// any order), which selects what shows inside it.
    /// </summary>
    public (Vector2 From, Vector2 To)? ScreenBox { get; private set; }

    public double CursorRadius => 0;

    private IReadOnlyList<SelectionFilterItem>? _filters;

    /// <summary>
    /// The kinds it picks as check boxes of its options (the selection filter): unchecked, a kind
    /// is locked. Empty for a tool of one kind.
    /// </summary>
    public IReadOnlyList<SelectionFilterItem> Filters => _filters ??= Commands.SelectionKinds.All.Count(kind => Kinds.HasFlag(kind)) > 1
        ? [.. Commands.SelectionKinds.All.Where(kind => Kinds.HasFlag(kind)).Select(kind => new SelectionFilterItem(model, kind))]
        : [];

    public bool HasFilters => Filters.Count > 0;

    partial void OnKindsChanged(ObjectKinds value)
    {
        _filters = null;
        OnPropertyChanged(nameof(Filters));
        OnPropertyChanged(nameof(HasFilters));
    }

    /// <summary>
    /// What the options bar says about using it.
    /// </summary>
    public string Help => "Click to select, shift adds, control toggles; drag a box on empty ground; drag the selection or its handles to move, turn or scale it (shift snaps, hold X or Z for one axis, R to turn); Q and E turn it, the arrows move it, Delete removes it";

    /// <summary>
    /// The handle under the mouse when a view last drew, or the one being dragged.
    /// </summary>
    public HandlePart Hovered { get; private set; }

    /// <summary>
    /// The handle being dragged; none between drags.
    /// </summary>
    public HandlePart Dragged => _gesture == Gesture.Handle ? _part : default;

    /// <summary>
    /// The box the selection's handles sit on, or null without a selection.
    /// </summary>
    public SelectionFrame? Frame
    {
        get
        {
            if (model.ShownDocument is not MapDocument document || model.Selection.IsEmpty)
            {
                return null;
            }
            ImmutableArray<ObjectRef> items = model.Selection.Items;
            if (_frameOf is not { } made || !ReferenceEquals(made.Document, document) || made.Items != items)
            {
                using Performance.Span measure = Performance.Measure("tool.selection-frame");
                made = (document, items, SelectionFrame.Of(document, items));
                _frameOf = made;
            }
            return made.Frame;
        }
    }

    /// <summary>
    /// The handle at the mouse of a press, or none: none while the handles are hidden or nothing is selected.
    /// </summary>
    public HandlePart HandleAt(float x, float z, ToolInput input)
    {
        if (!model.Handles.Show || Frame is not SelectionFrame frame)
        {
            return default;
        }
        ToolView view = input.ViewAt(x, z);
        return view.Mouse is Vector2 mouse
            ? HandleLayout.Of(frame, view.OgridsPerPixelAt(frame.Centre)).HitTest(mouse, view.ToScreen)
            : default;
    }

    /// <summary>
    /// The selection's handles at their size on screen: the box, the ring, the arrows, the move
    /// square and the scale squares, the one under the mouse (or dragged) lighter.
    /// </summary>
    public IReadOnlyList<ToolShape> Shapes(ToolView view)
    {
        if (ScreenBox is (Vector2 boxFrom, Vector2 boxTo) && !view.IsTopDown)
        {
            // the box of the 3D view, on screen
            Vector2[] corners = [boxFrom, new Vector2(boxTo.X, boxFrom.Y), boxTo, new Vector2(boxFrom.X, boxTo.Y)];
            return [new ToolLine(corners, true, 3, ToolInk.Shadow), new ToolLine(corners, true, 1, ToolInk.Selection)];
        }
        if (!model.Handles.Show || Frame is not SelectionFrame frame)
        {
            Hovered = default;
            return [];
        }
        using Performance.Span measure = Performance.Measure("tool.handle-shapes");
        HandleLayout layout = HandleLayout.Of(frame, view.OgridsPerPixelAt(frame.Centre));
        HandlePart hovered = _gesture == Gesture.Handle ? _part
            : _gesture == Gesture.None && view.Mouse is Vector2 mouse ? layout.HitTest(mouse, view.ToScreen)
            : default;
        Hovered = hovered;
        List<ToolShape> shapes = [];
        Vector2[]? Points(IEnumerable<Vector3> places)
        {
            List<Vector2> points = [];
            foreach (Vector3 place in places)
            {
                if (view.ToScreen(place) is not Vector2 point)
                {
                    return null;
                }
                points.Add(point);
            }
            return [.. points];
        }
        ToolInk Ink(HandlePart part, ToolInk ink) => part == hovered ? ToolInk.Hover : ink;
        if (frame.Scales && Points(layout.Box) is Vector2[] box)
        {
            shapes.Add(new ToolLine(box, true, 3, ToolInk.Shadow));
            shapes.Add(new ToolLine(box, true, 1, ToolInk.Selection));
        }
        if (frame.Turns && Points(layout.Ring()) is Vector2[] ring)
        {
            shapes.Add(new ToolLine(ring, true, 4, ToolInk.Shadow));
            shapes.Add(new ToolLine(ring, true, 2, Ink(new HandlePart(HandleKind.Turn), ToolInk.Selection)));
        }
        foreach ((HandleKind kind, (Vector3 From, Vector3 To) arrow, ToolInk ink) in new[] { (HandleKind.MoveX, layout.ArrowX, ToolInk.AxisX), (HandleKind.MoveZ, layout.ArrowZ, ToolInk.AxisZ) })
        {
            if (view.ToScreen(arrow.From) is Vector2 from && view.ToScreen(arrow.To) is Vector2 to && Vector2.Distance(from, to) > 4)
            {
                ToolInk shown = Ink(new HandlePart(kind), ink);
                Vector2 along = Vector2.Normalize(to - from);
                Vector2 across = new Vector2(-along.Y, along.X);
                shapes.Add(new ToolLine([from, to], false, 4, ToolInk.Shadow));
                shapes.Add(new ToolLine([from, to - along * 6], false, 2, shown));
                shapes.Add(new ToolTriangle(to + along * 3, to - along * 7 + across * 5, to - along * 7 - across * 5, shown));
            }
        }
        foreach ((HandlePart part, Vector3 place) in layout.Scales)
        {
            if (view.ToScreen(place) is Vector2 at)
            {
                shapes.Add(new ToolSquare(at, HandleLayout.ScaleHalf, part == hovered ? ToolInk.Hover : ToolInk.Fill, ToolInk.Selection));
            }
        }
        if (view.ToScreen(frame.Centre) is Vector2 centre)
        {
            HandlePart move = new HandlePart(HandleKind.Move);
            shapes.Add(new ToolSquare(centre, HandleLayout.MoveHalf, move == hovered ? ToolInk.Hover : ToolInk.Fill, ToolInk.Selection));
        }
        return shapes;
    }

    public void Press(float x, float z) => Press(x, z, ToolInput.Default);

    public void Drag(float x, float z) => Drag(x, z, _input);

    public void Press(float x, float z, ToolInput input)
    {
        _input = input;
        _from = (x, z);
        _dragging = false;
        _gesture = Gesture.None;
        _frame = null;
        if (model.ShownDocument is null)
        {
            return;
        }
        SelectionChange change = ChangeOf(input.Modifiers);
        ObjectRef? under = input.Picks
            ? input.Picked is ObjectRef found && found.IsIn(model.SelectableKinds, found.Kind == ObjectKind.Marker && model.ShownDocument!.Markers.PathNodes.Any(node => node.Id == found.Id)) ? found : null
            : model.PickAt(x, z, input.OgridsPerPixel, model.SelectableKinds);
        // the handles before anything under them; a click on a handle without a drag clicks what lies there
        _clickUnder = under;
        if (input.Modifiers.HasFlag(ToolModifiers.Turn) && Frame is SelectionFrame turned)
        {
            // R held: dragging turns the selection about its middle (or each thing about its own)
            _view = input.ViewAt(x, z);
            _frame = turned;
            _pressPlane = _view.PlaneAt(turned.Centre.Y, new Vector2(x, z)) ?? new Vector2(x, z);
            _gesture = Gesture.Turn;
            return;
        }
        if (HandleAt(x, z, input) is { IsNone: false } part && Frame is SelectionFrame frame)
        {
            _view = input.ViewAt(x, z);
            _part = part;
            _frame = frame;
            _pressPlane = _view.PlaneAt(frame.Centre.Y, new Vector2(x, z)) ?? new Vector2(x, z);
            _gesture = Gesture.Handle;
            return;
        }
        if (under is ObjectRef picked)
        {
            if (change == SelectionChange.Toggle)
            {
                model.Selection.Apply([picked], SelectionChange.Toggle);
                _gesture = Gesture.Click;
            }
            else
            {
                if (!model.Selection.Contains(picked))
                {
                    model.Selection.Apply([picked], change);
                }
                _gesture = Gesture.Move;
            }
        }
        else
        {
            _gesture = Gesture.Box;
            // in 3D the box is a rectangle on screen
            _screenFrom = input.View is { IsTopDown: false, Mouse: Vector2 mouse } ? mouse : null;
        }
    }

    public void Drag(float x, float z, ToolInput input)
    {
        if (!_dragging)
        {
            float distance = MathF.Sqrt((x - _from.X) * (x - _from.X) + (z - _from.Z) * (z - _from.Z));
            if (distance < DragPixels * _input.OgridsPerPixel)
            {
                return;
            }
            _dragging = true;
        }
        switch (_gesture)
        {
            case Gesture.Move:
                MoveTo(x, z, input.Modifiers);
                break;
            case Gesture.Handle:
                DragHandle(x, z, input);
                break;
            case Gesture.Turn:
                TurnTo(x, z, input);
                break;
            case Gesture.Box when _screenFrom is Vector2 from:
                if (input.View is { Mouse: Vector2 mouse } view)
                {
                    ScreenBox = (from, mouse);
                    _screenView = view;
                }
                break;
            case Gesture.Box:
                Box = (_from.X, _from.Z, x, z);
                break;
        }
    }

    public void Release()
    {
        switch (_gesture)
        {
            case Gesture.Move or Gesture.Handle or Gesture.Turn when _move is not null:
                model.EndStroke(_move);
                break;
            case Gesture.Handle when !_dragging:
                // a click on a handle: what a click there picks
                Click(_clickUnder, ChangeOf(_input.Modifiers));
                break;
            case Gesture.Box when Box is (float x0, float z0, float x1, float z1):
                model.SelectInside(x0, z0, x1, z1, ChangeOf(_input.Modifiers));
                break;
            case Gesture.Box when ScreenBox is (Vector2 from, Vector2 to) && _screenView is ToolView view:
                model.SelectOnScreen(from, to, view.ToScreen, ChangeOf(_input.Modifiers));
                break;
            case Gesture.Box when ChangeOf(_input.Modifiers) == SelectionChange.Replace:
                // a click on nothing
                model.Selection.Clear();
                break;
        }
        _gesture = Gesture.None;
        _move = null;
        _before = null;
        _moving = [];
        _partners = [];
        _frame = null;
        _view = null;
        _part = default;
        Box = null;
        ScreenBox = null;
        _screenFrom = null;
        _screenView = null;
    }

    public bool Key(string key, ToolModifiers modifiers)
    {
        bool shift = modifiers.HasFlag(ToolModifiers.Shift);
        if (key == "Escape")
        {
            if (model.Selection.IsEmpty)
            {
                return false;
            }
            _ = model.RunWithAsync("select.none", []);
            return true;
        }
        if (model.Selection.IsEmpty || modifiers.HasFlag(ToolModifiers.Control) || modifiers.HasFlag(ToolModifiers.Alt))
        {
            return false;
        }
        float step = shift ? 8 : 1;
        float turn = shift || model.SelectionTurnsInQuarters ? 90 : 15;
        (string Command, JsonObject Arguments)? run = key switch
        {
            "Q" => ("edit.rotate", new JsonObject { ["degrees"] = -turn }),
            "E" => ("edit.rotate", new JsonObject { ["degrees"] = turn }),
            "Left" => ("edit.nudge", new JsonObject { ["dx"] = -step, ["dz"] = 0 }),
            "Right" => ("edit.nudge", new JsonObject { ["dx"] = step, ["dz"] = 0 }),
            "Up" => ("edit.nudge", new JsonObject { ["dx"] = 0, ["dz"] = -step }),
            "Down" => ("edit.nudge", new JsonObject { ["dx"] = 0, ["dz"] = step }),
            _ => null,
        };
        if (run is not (string command, JsonObject arguments))
        {
            return false;
        }
        _ = model.RunWithAsync(command, arguments);
        return true;
    }

    /// <summary>
    /// Moves the selection by how far the mouse went since it was pressed: from the document as it
    /// was then, so the step of undo holds the whole drag.
    /// </summary>
    private void MoveTo(float x, float z, ToolModifiers modifiers)
    {
        using Performance.Span measure = Performance.Measure("tool.move-selection");
        if (!Begin("Move the selection"))
        {
            return;
        }
        Vector2 delta = HandleMaths.MoveDelta(_frame?.Middle ?? new Vector2(_from.X, _from.Z), new Vector2(x - _from.X, z - _from.Z), AxisOf(modifiers), SnapOf(modifiers));
        // with the symmetry, the mirror partners as they were at the press move along (F-12)
        Apply(before => before.MoveObjects(_moving, _partners, delta.X, delta.Y));
    }

    /// <summary>
    /// Moves, turns or scales the selection by how far the mouse went on the level of its handles
    /// since the press, from the document as it was then.
    /// </summary>
    private void DragHandle(float x, float z, ToolInput input)
    {
        using Performance.Span measure = Performance.Measure("tool.drag-handle");
        SelectionFrame frame = _frame!;
        ToolView view = input.View ?? _view ?? input.ViewAt(x, z);
        if (view.PlaneAt(frame.Centre.Y, new Vector2(x, z)) is not Vector2 place)
        {
            return;
        }
        string description = _part.Kind switch
        {
            HandleKind.Turn => "Turn the selection",
            HandleKind.Scale => "Scale the selection",
            _ => "Move the selection",
        };
        if (!Begin(description))
        {
            return;
        }
        Vector2? pivot = model.Handles.Pivot == HandlePivot.Selection ? frame.Middle : null;
        switch (_part.Kind)
        {
            case HandleKind.Turn:
                float step = model.SelectionTurnsInQuarters ? MathF.PI / 2
                    : input.Modifiers.HasFlag(ToolModifiers.Shift) ? HandleSettings.SnapDegrees * MathF.PI / 180 : 0;
                float angle = HandleMaths.TurnAngle(frame.Middle, _pressPlane, place, step);
                Apply(before => pivot is Vector2 p ? before.RotateObjectsAbout(_moving, _partners, angle, p) : before.RotateObjects(_moving, _partners, angle));
                break;
            case HandleKind.Scale:
                Vector2 factors = HandleMaths.ScaleFactors(frame, _part, _pressPlane, place, input.Modifiers.HasFlag(ToolModifiers.Shift) ? HandleSettings.ScaleSnap : 0);
                Apply(before => before.ScaleObjects(_moving, _partners, factors.X, factors.Y, frame.Heading, pivot));
                break;
            default:
                HandleAxis axis = _part.Kind switch
                {
                    HandleKind.MoveX => HandleAxis.X,
                    HandleKind.MoveZ => HandleAxis.Z,
                    _ => AxisOf(input.Modifiers),
                };
                Vector2 delta = HandleMaths.MoveDelta(frame.Middle, place - _pressPlane, axis, SnapOf(input.Modifiers));
                Apply(before => before.MoveObjects(_moving, _partners, delta.X, delta.Y));
                break;
        }
    }

    /// <summary>
    /// Turns the selection by the angle the mouse went round its middle since the press (R held):
    /// shift snaps to 15 degrees, structures alone turn in quarter turns.
    /// </summary>
    private void TurnTo(float x, float z, ToolInput input)
    {
        using Performance.Span measure = Performance.Measure("tool.turn-selection");
        SelectionFrame frame = _frame!;
        ToolView view = input.View ?? _view ?? input.ViewAt(x, z);
        if (view.PlaneAt(frame.Centre.Y, new Vector2(x, z)) is not Vector2 place || !Begin("Turn the selection"))
        {
            return;
        }
        float step = model.SelectionTurnsInQuarters ? MathF.PI / 2
            : input.Modifiers.HasFlag(ToolModifiers.Shift) ? HandleSettings.SnapDegrees * MathF.PI / 180 : 0;
        float angle = HandleMaths.TurnAngle(frame.Middle, _pressPlane, place, step);
        Vector2? pivot = model.Handles.Pivot == HandlePivot.Selection ? frame.Middle : null;
        Apply(before => pivot is Vector2 p ? before.RotateObjectsAbout(_moving, _partners, angle, p) : before.RotateObjects(_moving, _partners, angle));
    }

    /// <summary>
    /// Starts the drag's step of undo the first time it changes something; false when it cannot
    /// (nothing selected, another stroke) or the editor ended it (another map was opened).
    /// </summary>
    private bool Begin(string description)
    {
        if (_move is null)
        {
            if (model.Selection.IsEmpty || model.StrokeInProgress is not null)
            {
                _gesture = Gesture.None;
                return false;
            }
            _moving = model.Selection.Items;
            if (_gesture == Gesture.Move && _clickUnder is ObjectRef grabbed && _moving.Contains(grabbed))
            {
                // the thing under the mouse leads: with the symmetry its selected partners follow it mirrored
                _moving = [grabbed, .. _moving.Where(thing => thing != grabbed)];
            }
            _frame ??= Frame;
            _move = model.StartStroke(description);
            _before = _move.Document;
            // with the symmetry, the mirror partners as they were at the press move along (F-12)
            _partners = model.PartnersOf(_before, _moving);
        }
        return model.StrokeInProgress == _move;
    }

    private void Apply(Func<MapDocument, MapDocument> edit)
    {
        MapDocument before = _before!;
        _move!.Apply(_ => edit(before));
        model.ShowStroke();
    }

    private void Click(ObjectRef? under, SelectionChange change)
    {
        if (under is ObjectRef thing)
        {
            model.Selection.Apply([thing], change);
        }
        else if (change == SelectionChange.Replace)
        {
            model.Selection.Clear();
        }
    }

    private static HandleAxis AxisOf(ToolModifiers modifiers) =>
        modifiers.HasFlag(ToolModifiers.AxisX) ? HandleAxis.X
        : modifiers.HasFlag(ToolModifiers.AxisZ) ? HandleAxis.Z
        : HandleAxis.Both;

    private float SnapOf(ToolModifiers modifiers) => modifiers.HasFlag(ToolModifiers.Shift) ? model.Handles.Snap.Step : 0;

    private static SelectionChange ChangeOf(ToolModifiers modifiers) =>
        modifiers.HasFlag(ToolModifiers.Control) ? SelectionChange.Toggle
        : modifiers.HasFlag(ToolModifiers.Shift) ? SelectionChange.Add
        : SelectionChange.Replace;
}
