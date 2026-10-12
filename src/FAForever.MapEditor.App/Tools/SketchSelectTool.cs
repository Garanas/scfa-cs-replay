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
/// The Sketch mode's Select tool (user story N-4): a click selects the stroke, symbol or label under the
/// mouse in the chosen sheet, a drag on nothing draws a box that selects what it crosses, Shift adds (or
/// takes away what a click meets). A drag on the selection moves it, as one step of undo from press to
/// release. Delete removes the selection, a colour key (1 to 6) or the row recolours it, Ctrl+A selects the
/// whole sheet, Escape selects nothing, and a double click on a label opens its text for editing. The
/// selection is the tool's, not an edit of the map; a sheet chosen or an item that is no longer there
/// ends it. The commands <c>sketch.select</c>, <c>sketch.move</c>, <c>sketch.recolour</c>, <c>sketch.resize</c>
/// and <c>sketch.deleteSelected</c> do what the mouse and keys do.
/// </summary>
public sealed partial class SketchSelectTool : ObservableObject, IMapTool
{
    /// <summary>
    /// How near an item, in pixels, the mouse must be to take it.
    /// </summary>
    private const float ReachPixels = 6;

    /// <summary>
    /// How far the mouse must go, in pixels, before a press becomes a drag.
    /// </summary>
    private const float DragPixels = 3;

    /// <summary>
    /// Items past this many are drawn as one outline round all of them.
    /// </summary>
    private const int OutlinedItems = 400;

    private enum Mode
    {
        None,
        Move,
        Box,
    }

    private readonly MainViewModel _model;
    private ImmutableArray<SketchItem> _selected = [];
    private string _sheet = "";
    private ToolInput _input = ToolInput.Default;
    private Mode _mode;
    private (float X, float Z) _from;
    private (float X, float Z) _to;
    private bool _dragging;
    private bool _shift;
    private EditTransaction? _edit;
    private MapDocument? _before;
    private ImmutableArray<SketchItem> _moving = [];
    private (float Dx, float Dz) _moved;

    public SketchSelectTool(MainViewModel model)
    {
        _model = model;
        Colours = [.. Enum.GetValues<SketchColour>().Select(colour => new SketchColourChoice(colour, Recolour, "Recolour the selection"))];
        Sizes = [.. Enum.GetValues<SketchSize>().Select(size => new SketchSizeChoice(size, Resize, "selected symbols and labels"))];
        model.Sketch.ChosenChanged += (_, _) =>
        {
            Clear();
            OnPropertyChanged(nameof(SheetText));
        };
    }

    /// <summary>
    /// The six colours of the row: each recolours the selection.
    /// </summary>
    public IReadOnlyList<SketchColourChoice> Colours { get; }

    /// <summary>
    /// The three sizes of the row: each sizes the selected symbols and labels.
    /// </summary>
    public IReadOnlyList<SketchSizeChoice> Sizes { get; }

    private Task Recolour(SketchColour colour) => _model.RunWithAsync("sketch.recolour", new JsonObject { ["colour"] = colour.ToString() });

    private Task Resize(SketchSize size) => _model.RunWithAsync("sketch.resize", new JsonObject { ["size"] = size.ToString() });

    public double CursorRadius => 0;

    public string SheetText => _model.Sketch.Chosen is string sheet ? $"Sheet: {sheet}" : "No sheet yet";

    /// <summary>
    /// The sheet the selection is in.
    /// </summary>
    public string Sheet => _sheet;

    /// <summary>
    /// The selected items, those the sheet still has.
    /// </summary>
    public ImmutableArray<SketchItem> Selected
    {
        get
        {
            if (_selected.IsEmpty)
            {
                return _selected;
            }
            SketchSheet? sheet = _model.ShownDocument?.Sketch.Find(_sheet);
            if (sheet is null)
            {
                return [];
            }
            ImmutableArray<SketchItem> held = [.. _selected.Where(item => SketchPicking.Exists(sheet, item))];
            if (held.Length != _selected.Length)
            {
                _selected = held;
                Changed(redraw: false);
            }
            return held;
        }
    }

    /// <summary>
    /// What the options row says of the selection: <c>2 strokes · 1 label selected</c>.
    /// </summary>
    public string Summary
    {
        get
        {
            ImmutableArray<SketchItem> items = Selected;
            return items.IsEmpty
                ? "Nothing selected"
                : $"{SketchSheetItem.Describe(items.Count(i => i.Kind == SketchItemKind.Stroke), items.Count(i => i.Kind == SketchItemKind.Symbol), items.Count(i => i.Kind == SketchItemKind.Label), " · ")} selected";
        }
    }

    public bool HasSelection => !_selected.IsEmpty;

    /// <summary>
    /// Selects items of a sheet, replacing the selection or adding to it.
    /// </summary>
    public void Select(string sheet, IEnumerable<SketchItem> items, bool add)
    {
        if (_sheet != sheet || !add)
        {
            _selected = [];
        }
        _sheet = sheet;
        _selected = [.. _selected.Union(items)];
        Changed();
    }

    /// <summary>
    /// Selects nothing.
    /// </summary>
    public void Clear()
    {
        if (_selected.IsEmpty)
        {
            return;
        }
        _selected = [];
        Changed();
    }

    private void Changed(bool redraw = true)
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(HasSelection));
        if (redraw)
        {
            _model.ShowStroke();
        }
    }

    /// <summary>
    /// The box being dragged, in ogrids; null when none is. (It is drawn by <see cref="Shapes"/> in the handle colour, not as the
    /// yellow box of the other select tools.)
    /// </summary>
    public (float X0, float Z0, float X1, float Z1)? DraggedBox => _mode == Mode.Box && _dragging ? (_from.X, _from.Z, _to.X, _to.Z) : null;

    public void Press(float x, float z) => Press(x, z, ToolInput.Default);

    public void Drag(float x, float z) => Drag(x, z, _input);

    public void Press(float x, float z, ToolInput input)
    {
        _input = input;
        _from = _to = (x, z);
        _mode = Mode.None;
        _dragging = false;
        _shift = input.Modifiers.HasFlag(ToolModifiers.Shift);
        if (_model.ShownDocument is not MapDocument document)
        {
            return;
        }
        if (_model.Sketch.Chosen is not string name || document.Sketch.Find(name) is not SketchSheet sheet)
        {
            _model.Status = "The sketch has no sheet yet";
            return;
        }
        if (_sheet != sheet.Name)
        {
            _selected = [];
            _sheet = sheet.Name;
        }
        SketchItem? hit = SketchPicking.Pick(sheet, new Vector2(x, z), ReachPixels * input.OgridsPerPixel);
        if (hit is not SketchItem item)
        {
            _mode = Mode.Box;
            return;
        }
        if (input.Clicks >= 2 && item.Kind == SketchItemKind.Label)
        {
            _ = EditLabel(sheet, item.Index, input);
            return;
        }
        ImmutableArray<SketchItem> held = Selected;
        if (_shift && held.Contains(item))
        {
            _selected = held.Remove(item);
            Changed();
            return;
        }
        if (_shift)
        {
            _selected = held.Add(item);
            Changed();
        }
        else if (!held.Contains(item))
        {
            _selected = [item];
            Changed();
        }
        _moving = Selected;
        _mode = Mode.Move;
    }

    public void Drag(float x, float z, ToolInput input)
    {
        if (_mode == Mode.None)
        {
            return;
        }
        if (!_dragging)
        {
            float distance = MathF.Sqrt((x - _from.X) * (x - _from.X) + (z - _from.Z) * (z - _from.Z));
            if (distance < DragPixels * _input.OgridsPerPixel)
            {
                return;
            }
            _dragging = true;
        }
        using Performance.Span measure = Performance.Measure("tool.sketch-select");
        _to = (x, z);
        if (_mode == Mode.Box)
        {
            _model.ShowStroke();
            return;
        }
        MoveTo(x - _from.X, z - _from.Z, input.Modifiers);
    }

    public void Release()
    {
        Mode mode = _mode;
        _mode = Mode.None;
        if (mode == Mode.Move)
        {
            if (_edit is EditTransaction edit)
            {
                _edit = null;
                _before = null;
                _model.EndStroke(edit);
                _model.Status = $"Moved {_moving.Length} {(_moving.Length == 1 ? "item" : "items")} of {_sheet} by {_moved.Dx:0.#}, {_moved.Dz:0.#} ogrids";
            }
            _dragging = false;
            return;
        }
        if (mode == Mode.Box && _model.ShownDocument?.Sketch.Find(_sheet) is SketchSheet sheet)
        {
            if (_dragging)
            {
                ImmutableArray<SketchItem> inside = SketchPicking.InBox(sheet, new Vector2(_from.X, _from.Z), new Vector2(_to.X, _to.Z));
                _selected = _shift ? [.. Selected.Union(inside)] : inside;
                _model.Status = $"Selected {_selected.Length} {(_selected.Length == 1 ? "item" : "items")} of {_sheet}";
                _dragging = false;
                Changed();
            }
            else if (!_shift)
            {
                // a click on nothing
                _dragging = false;
                Clear();
            }
        }
        _dragging = false;
    }

    // the selection moved by an offset from where it was when the mouse went down: the step of undo holds the whole drag
    private void MoveTo(float dx, float dz, ToolModifiers modifiers)
    {
        if (modifiers.HasFlag(ToolModifiers.AxisX))
        {
            dz = 0;
        }
        else if (modifiers.HasFlag(ToolModifiers.AxisZ))
        {
            dx = 0;
        }
        if (_edit is null)
        {
            if (_model.StrokeInProgress is not null)
            {
                _mode = Mode.None;
                return;
            }
            _edit = _model.StartStroke("Move in the sketch");
            _before = _edit.Document;
        }
        if (_model.StrokeInProgress != _edit)
        {
            return;
        }
        MapDocument before = _before!;
        string sheet = _sheet;
        ImmutableArray<SketchItem> items = _moving;
        _moved = (MathF.Round(dx, 2), MathF.Round(dz, 2));
        (float Dx, float Dz) moved = _moved;
        _edit.Apply(_ => before.MoveItems(sheet, items, moved.Dx, moved.Dz));
        _model.ShowStroke();
    }

    private async Task EditLabel(SketchSheet sheet, int index, ToolInput input)
    {
        SketchLabel label = sheet.Labels[index];
        ToolView view = input.ViewAt(label.Place.X, label.Place.Y);
        Vector2 screen = view.ToScreen(SketchPlaces.OnGround(view, _model.ShownDocument, label.Place.X, label.Place.Y)) ?? Vector2.Zero;
        _selected = [new SketchItem(SketchItemKind.Label, index)];
        Changed();
        await _model.Sketch.BeginLabel(new SketchLabelDraft(label.Place, screen, label.Colour, label.Size, sheet.Name, index) { ThreeD = !view.IsTopDown }, label.Text);
    }

    public bool Key(string key, ToolModifiers modifiers)
    {
        if (key == "Escape")
        {
            if (_edit is EditTransaction edit)
            {
                _edit = null;
                _mode = Mode.None;
                _model.EndStroke(edit, keep: false);
                _model.Status = "Cancelled the move";
                return true;
            }
            if (_dragging)
            {
                _dragging = false;
                _mode = Mode.None;
                _model.ShowStroke();
                return true;
            }
            if (HasSelection)
            {
                Clear();
                return true;
            }
            return false;
        }
        if (key == "Delete" && HasSelection)
        {
            _ = _model.RunWithAsync("sketch.deleteSelected", []);
            return true;
        }
        if (key == "A" && modifiers == ToolModifiers.Control)
        {
            _ = _model.RunWithAsync("sketch.select", new JsonObject { ["all"] = true });
            return true;
        }
        if (modifiers != ToolModifiers.None)
        {
            return false;
        }
        string digit = key.Length == 2 && key[0] == 'D' ? key[1..] : key.StartsWith("NumPad", StringComparison.Ordinal) && key.Length == 7 ? key[6..] : "";
        if (digit.Length == 1 && digit[0] is >= '1' and <= '6' && HasSelection)
        {
            _ = _model.RunWithAsync("sketch.recolour", new JsonObject { ["colour"] = (int)(digit[0] - '0') });
            return true;
        }
        return false;
    }

    /// <summary>
    /// The selection's outlines at a fixed size on screen: a dashed box round each selected item, over a dark
    /// rim, in the handle colour that no pencil has. A selection of very many items has one box round all.
    /// </summary>
    public IReadOnlyList<ToolShape> Shapes(ToolView view)
    {
        if (_model.ShownDocument is not MapDocument document || document.Sketch.Find(_sheet) is not SketchSheet sheet)
        {
            return [];
        }
        List<ToolShape> shapes = [];
        if (DraggedBox is { } box)
        {
            if (Outline(view, document, new Vector2(Math.Min(box.X0, box.X1), Math.Min(box.Z0, box.Z1)), new Vector2(Math.Max(box.X0, box.X1), Math.Max(box.Z0, box.Z1))) is Vector2[] corners)
            {
                shapes.Add(new ToolLine(corners, true, 3, ToolInk.Shadow));
                shapes.Add(new ToolLine(corners, true, 1.5f, ToolInk.Handle) { Dash = ToolDash.Dashed });
            }
        }
        ImmutableArray<SketchItem> items = Selected;
        if (items.IsEmpty)
        {
            return shapes;
        }
        if (items.Length > OutlinedItems)
        {
            Vector2 min = new Vector2(float.MaxValue);
            Vector2 max = new Vector2(float.MinValue);
            foreach (SketchItem item in items)
            {
                (Vector2 low, Vector2 high) = SketchPicking.Bounds(sheet, item);
                min = Vector2.Min(min, low);
                max = Vector2.Max(max, high);
            }
            AddBox(shapes, view, document, min, max);
            return shapes;
        }
        foreach (SketchItem item in items)
        {
            (Vector2 low, Vector2 high) = SketchPicking.Bounds(sheet, item);
            AddBox(shapes, view, document, low, high);
        }
        return shapes;
    }

    private static void AddBox(List<ToolShape> shapes, ToolView view, MapDocument document, Vector2 low, Vector2 high)
    {
        // a few pixels of air round the item, so a thin line is not hidden by its own outline
        float air = 3 * view.OgridsPerPixelAt(SketchPlaces.OnGround(view, document, low.X, low.Y));
        if (Outline(view, document, low - new Vector2(air), high + new Vector2(air)) is not Vector2[] corners)
        {
            return;
        }
        shapes.Add(new ToolLine(corners, true, 4, ToolInk.Shadow));
        shapes.Add(new ToolLine(corners, true, 2, ToolInk.Handle) { Dash = ToolDash.Dashed });
    }

    /// <summary>
    /// The outline of a box on the map, in the view's pixels: its four corners in the top view, in 3D the same edges
    /// along the ground (a few places on each, so they follow the hills); null while a corner is out of sight.
    /// </summary>
    private static Vector2[]? Outline(ToolView view, MapDocument document, Vector2 low, Vector2 high)
    {
        int steps = view.IsTopDown ? 1 : 12;
        List<Vector2> points = [];
        Vector2[] corners = [low, new Vector2(high.X, low.Y), high, new Vector2(low.X, high.Y)];
        for (int side = 0; side < 4; side++)
        {
            Vector2 from = corners[side];
            Vector2 to = corners[(side + 1) % 4];
            for (int step = 0; step < steps; step++)
            {
                Vector2 place = Vector2.Lerp(from, to, (float)step / steps);
                if (view.ToScreen(SketchPlaces.OnGround(view, document, place.X, place.Y)) is not Vector2 screen)
                {
                    return null;
                }
                points.Add(screen);
            }
        }
        return [.. points];
    }

    /// <summary>
    /// What the tool says for automation: the sheet, the selected items and what a drag is doing.
    /// </summary>
    public JsonObject Describe() => new JsonObject
    {
        ["sheet"] = _sheet,
        ["selected"] = new JsonArray([.. Selected.Select(item => (JsonNode)new JsonObject
        {
            ["kind"] = item.Kind.ToString().ToLowerInvariant(),
            ["index"] = item.Index,
        })]),
        ["count"] = Selected.Length,
        ["summary"] = Summary,
        ["dragging"] = _dragging,
        ["box"] = DraggedBox is { } box ? new JsonArray(Math.Round(box.X0, 2), Math.Round(box.Z0, 2), Math.Round(box.X1, 2), Math.Round(box.Z1, 2)) : null,
    };
}
