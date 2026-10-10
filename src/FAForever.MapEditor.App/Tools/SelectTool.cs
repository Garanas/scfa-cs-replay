using System.Collections.Immutable;
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
/// and control as a click). Dragging a selected thing moves the whole selection, one step of undo,
/// keeping each thing on the terrain. Its keys in a view: Escape selects nothing, Q and E turn the
/// selection by 15 degrees (with shift, or when only structures are selected, 90), the arrows move
/// it an ogrid (with shift 8).
/// </summary>
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
    }

    private Gesture _gesture;
    private ToolInput _input = ToolInput.Default;
    private (float X, float Z) _from;
    private bool _dragging;
    private EditTransaction? _move;
    private MapDocument? _before;
    private ImmutableArray<ObjectRef> _moving = [];
    private ImmutableArray<SymmetryPartner> _partners = [];

    /// <summary>
    /// The kinds it picks, of those the views show: the Markers mode's tool markers and areas, the
    /// Units mode's units. A tool that selects everything takes <see cref="ObjectKinds.All"/>.
    /// </summary>
    [ObservableProperty]
    public partial ObjectKinds Kinds { get; set; } = kinds;

    /// <summary>
    /// The box being drawn, while it is.
    /// </summary>
    [ObservableProperty]
    public partial (float X0, float Z0, float X1, float Z1)? Box { get; private set; }

    public double CursorRadius => 0;

    /// <summary>
    /// What the options bar says about using it.
    /// </summary>
    public string Help => "Click to select, shift adds, control toggles; drag a box on empty ground; drag the selection to move it; Q and E turn it, the arrows move it, Delete removes it";

    public void Press(float x, float z) => Press(x, z, ToolInput.Default);

    public void Drag(float x, float z) => Drag(x, z, _input);

    public void Press(float x, float z, ToolInput input)
    {
        _input = input;
        _from = (x, z);
        _dragging = false;
        _gesture = Gesture.None;
        if (model.ShownDocument is null)
        {
            return;
        }
        SelectionChange change = ChangeOf(input.Modifiers);
        ObjectRef? under = input.Picks
            ? input.Picked is ObjectRef found && found.IsIn(model.SelectableKinds, found.Kind == ObjectKind.Marker && model.ShownDocument!.Markers.PathNodes.Any(node => node.Id == found.Id)) ? found : null
            : model.PickAt(x, z, input.OgridsPerPixel, model.SelectableKinds);
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
                MoveTo(x, z);
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
            case Gesture.Move when _move is not null:
                model.EndStroke(_move);
                break;
            case Gesture.Box when Box is (float x0, float z0, float x1, float z1):
                model.SelectInside(x0, z0, x1, z1, ChangeOf(_input.Modifiers));
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
        Box = null;
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
    private void MoveTo(float x, float z)
    {
        using Performance.Span measure = Performance.Measure("tool.move-selection");
        if (_move is null)
        {
            if (model.Selection.IsEmpty || model.StrokeInProgress is not null)
            {
                _gesture = Gesture.None;
                return;
            }
            _moving = model.Selection.Items;
            _move = model.StartStroke("Move the selection");
            _before = _move.Document;
            // with the symmetry, the mirror partners as they were at the press move along (F-12)
            _partners = model.PartnersOf(_before, _moving);
        }
        if (model.StrokeInProgress != _move)
        {
            // the editor ended it: another map was opened
            return;
        }
        MapDocument before = _before!;
        ImmutableArray<ObjectRef> moving = _moving;
        ImmutableArray<SymmetryPartner> partners = _partners;
        float dx = x - _from.X;
        float dz = z - _from.Z;
        _move.Apply(_ => before.MoveObjects(moving, partners, dx, dz));
        model.ShowStroke();
    }

    private static SelectionChange ChangeOf(ToolModifiers modifiers) =>
        modifiers.HasFlag(ToolModifiers.Control) ? SelectionChange.Toggle
        : modifiers.HasFlag(ToolModifiers.Shift) ? SelectionChange.Add
        : SelectionChange.Replace;
}
