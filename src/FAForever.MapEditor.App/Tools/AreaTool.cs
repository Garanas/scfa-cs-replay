using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tools;

/// <summary>
/// Draws, moves and resizes the map's areas (user story M-5). Dragging on empty ground draws a new
/// area (<c>map.addArea</c>); dragging a handle of an area (its corners and the middles of its
/// edges, drawn for the selected areas) resizes it, dragging its edge or inside moves it, each one
/// step of undo held from press to release; a click selects the area under the mouse, or nothing.
/// Edges move by whole ogrids. Its keys in a view: Delete removes the selected areas, Escape
/// selects nothing.
/// </summary>
public sealed partial class AreaTool(MainViewModel model) : ObservableObject, IMapTool
{
    /// <summary>
    /// How near a handle, in pixels, the mouse must be to take it.
    /// </summary>
    private const float HandlePixels = 6;

    private const float DragPixels = 3;

    private ToolInput _input = ToolInput.Default;
    private (float X, float Z) _from;
    private bool _dragging;
    private bool _drawing;
    private string? _area;
    private Area? _original;
    private AreaHandle _handle;
    private EditTransaction? _edit;
    private MapDocument? _before;

    public double CursorRadius => 0;

    /// <summary>
    /// The rectangle of a new area while it is drawn.
    /// </summary>
    [ObservableProperty]
    public partial (float X0, float Z0, float X1, float Z1)? Box { get; private set; }

    /// <summary>
    /// The handles of the selected areas.
    /// </summary>
    public ToolMarks? Marks
    {
        get
        {
            if (model.ShownDocument is not MapDocument document)
            {
                return null;
            }
            List<(float X, float Z)> handles = [];
            foreach (ObjectRef thing in model.Selection.Items)
            {
                if (thing.Kind == ObjectKind.Area && document.Find(thing) is Area area)
                {
                    handles.AddRange(AreaEdits.Handles(area).Select(handle => ((float)handle.X, (float)handle.Z)));
                }
            }
            return handles.Count == 0 ? null : new ToolMarks(handles);
        }
    }

    /// <summary>
    /// What the options bar says about using it.
    /// </summary>
    public string Help => "Drag on empty ground to draw an area; drag a corner or edge of the selected area to resize it, inside it to move it; Delete removes it. The inspector's check box makes the selected area the playable area";

    public void Press(float x, float z) => Press(x, z, ToolInput.Default);

    public void Drag(float x, float z) => Drag(x, z, _input);

    public void Press(float x, float z, ToolInput input)
    {
        _input = input;
        _from = (x, z);
        _dragging = false;
        _drawing = false;
        _area = null;
        if (model.ShownDocument is not MapDocument document || !model.ShowAreas)
        {
            _drawing = model.ShownDocument is not null;
            return;
        }
        float reach = HandlePixels * input.OgridsPerPixel;
        // the handles of the selected areas first, then any area under the mouse
        IEnumerable<Area> selected = model.Selection.Items.Where(thing => thing.Kind == ObjectKind.Area).Select(document.Find).OfType<Area>();
        (Area Area, AreaHandle Handle)? hit = selected.Select(area => (area, AreaEdits.HandleAt(area, x, z, reach)))
            .Where(candidate => candidate.Item2 != AreaHandle.None)
            .OrderBy(candidate => candidate.Item2 == AreaHandle.Move)
            .Select(candidate => ((Area, AreaHandle)?)candidate)
            .FirstOrDefault();
        if (hit is null && model.PickAt(x, z, input.OgridsPerPixel, ObjectKinds.Areas) is ObjectRef picked && document.Find(picked) is Area under)
        {
            hit = (under, AreaHandle.Move);
        }
        if (hit is (Area area, AreaHandle handle))
        {
            ObjectRef thing = ObjectRef.Area(area.Name);
            if (!model.Selection.Contains(thing))
            {
                model.Selection.Apply([thing], input.Modifiers.HasFlag(ToolModifiers.Shift) ? SelectionChange.Add : SelectionChange.Replace);
            }
            model.InspectTheSelection();
            _area = area.Name;
            _original = area;
            _handle = handle;
        }
        else
        {
            _drawing = true;
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
        if (_drawing)
        {
            Box = (MathF.Round(_from.X), MathF.Round(_from.Z), MathF.Round(x), MathF.Round(z));
        }
        else if (_area is not null)
        {
            Reshape(x, z);
        }
    }

    public void Release()
    {
        if (_edit is not null)
        {
            model.EndStroke(_edit);
        }
        else if (_drawing && Box is (float x0, float z0, float x1, float z1) && Math.Abs(x1 - x0) >= 1 && Math.Abs(z1 - z0) >= 1)
        {
            _ = model.RunWithAsync("map.addArea", new JsonObject { ["x0"] = x0, ["z0"] = z0, ["x1"] = x1, ["z1"] = z1 });
        }
        else if (_drawing && !_dragging && !_input.Modifiers.HasFlag(ToolModifiers.Shift))
        {
            // a click on nothing
            model.Selection.Clear();
        }
        _edit = null;
        _before = null;
        _area = null;
        _original = null;
        _drawing = false;
        Box = null;
    }

    public bool Key(string key, ToolModifiers modifiers)
    {
        bool hasAreas = model.Selection.Items.Any(thing => thing.Kind == ObjectKind.Area);
        switch (key)
        {
            case "Escape" when !model.Selection.IsEmpty:
                _ = model.RunWithAsync("select.none", []);
                return true;
            case "Delete" when hasAreas:
                _ = model.RunWithAsync("edit.delete", []);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Moves what the handle moves by how far the mouse went since the press, in whole ogrids:
    /// from the document as it was then, so the step of undo holds the whole drag.
    /// </summary>
    private void Reshape(float x, float z)
    {
        using Performance.Span measure = Performance.Measure("tool.reshape-area");
        if (_edit is null)
        {
            if (model.StrokeInProgress is not null)
            {
                _area = null;
                return;
            }
            _edit = model.StartStroke(_handle == AreaHandle.Move ? $"Move the area {_area}" : $"Resize the area {_area}");
            _before = _edit.Document;
        }
        if (model.StrokeInProgress != _edit)
        {
            return;
        }
        MapDocument before = _before!;
        Area original = _original!;
        string name = _area!;
        AreaHandle handle = _handle;
        double dx = Math.Round(x - _from.X);
        double dz = Math.Round(z - _from.Z);
        _edit.Apply(_ => before.UpdateArea(name, _ => AreaEdits.Drag(original, handle, dx, dz)));
        model.ShowStroke();
    }
}
