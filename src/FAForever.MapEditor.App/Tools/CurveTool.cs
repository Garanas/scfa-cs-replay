using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tools;

/// <summary>
/// Draws and edits curves that shape the terrain (user story T-4). A curve is a height layer (T-5):
/// while the chosen layer is a curve, a click adds a point at its end (shift: into the nearest
/// part of it), dragging a point moves it and the terrain follows, control and a click removes a
/// point; while it is not, the first click makes a new curve layer with the tool's profile, width,
/// falloff and height. Escape ends the curve: the next click starts another. The top view draws
/// the curve and its points (<see cref="Marks"/>).
/// </summary>
public sealed partial class CurveTool(MainViewModel model) : ObservableObject, IMapTool
{
    /// <summary>
    /// How near a point, in pixels, the mouse must be to take it.
    /// </summary>
    private const float HandlePixels = 7;

    private ToolInput _input = ToolInput.Default;
    private int _dragged = -1;
    private EditTransaction? _edit;
    private MapDocument? _before;
    private bool _startNew;

    public static IReadOnlyList<CurveProfile> Profiles { get; } = Enum.GetValues<CurveProfile>();

    /// <summary>
    /// The profile of a new curve: ridge, valley, road or cliff edge.
    /// </summary>
    [ObservableProperty]
    public partial CurveProfile Profile { get; set; } = CurveProfile.Ridge;

    /// <summary>
    /// The width of a new curve's part at full height, in ogrids.
    /// </summary>
    [ObservableProperty]
    public partial double CurveWidth { get; set; } = 8;

    /// <summary>
    /// How far beyond the width a new curve fades out, in ogrids.
    /// </summary>
    [ObservableProperty]
    public partial double Falloff { get; set; } = 12;

    /// <summary>
    /// How much a new ridge, valley or cliff rises or sinks, in game units; a road's points lie on
    /// the terrain where they are clicked.
    /// </summary>
    [ObservableProperty]
    public partial double Height { get; set; } = 10;

    public double CursorRadius => 0;

    public string Help => "Click to add points to the chosen curve (or start a new one); shift and a click inserts a point, drag a point to move it, control and a click removes it; Escape starts another curve";

    /// <summary>
    /// The chosen curve when it can be edited: a curve layer, not locked; null otherwise.
    /// </summary>
    public HeightLayer? Curve =>
        model.ShownDocument is MapDocument document && HeightLayers.Find(document, model.HeightStack.Chosen) is { Kind: HeightLayerKind.Curve, Locked: false, Curve: not null } layer
            ? layer
            : null;

    /// <summary>
    /// The chosen curve's line and points.
    /// </summary>
    public ToolMarks? Marks
    {
        get
        {
            if (Curve?.Curve is not HeightCurve curve || curve.Points.IsEmpty)
            {
                return null;
            }
            IReadOnlyList<CurvePoint> line = HeightCurves.Spline(curve);
            // a curve mirrored by the symmetry shows its images too (F-12)
            IReadOnlyList<IReadOnlyList<(float X, float Z)>>? images = curve.Symmetry is not null && model.ShownDocument is MapDocument document
                ? [.. HeightCurves.Parts(curve, document.Width, document.Height).Skip(1).Select(part => (IReadOnlyList<(float X, float Z)>)[.. HeightCurves.Spline(part).Select(point => (point.X, point.Z))])]
                : null;
            return new ToolMarks([.. curve.Points.Select(point => (point.X, point.Z))], Path: [.. line.Select(point => (point.X, point.Z))], Images: images);
        }
    }

    /// <summary>
    /// Another layer was chosen: the views draw its points.
    /// </summary>
    public void OnLayerChanged()
    {
        _startNew = false;
        OnPropertyChanged(nameof(Marks));
        OnPropertyChanged(nameof(Curve));
    }

    public void Press(float x, float z) => Press(x, z, ToolInput.Default);

    public void Drag(float x, float z) => Drag(x, z, _input);

    public void Press(float x, float z, ToolInput input)
    {
        _input = input;
        _dragged = -1;
        if (model.ShownDocument is null)
        {
            return;
        }
        HeightLayer? layer = _startNew ? null : Curve;
        if (layer?.Curve is not HeightCurve curve)
        {
            _startNew = false;
            _ = model.RunWithAsync("terrain.addCurve", new JsonObject
            {
                ["points"] = new JsonArray(new JsonArray(x, z)),
                ["profile"] = Profile.ToString(),
                ["width"] = CurveWidth,
                ["falloff"] = Falloff,
                ["height"] = Height,
            });
            return;
        }
        float reach = HandlePixels * input.OgridsPerPixel;
        int hit = Nearest(curve, x, z, reach);
        if (hit >= 0 && input.Modifiers.HasFlag(ToolModifiers.Control))
        {
            _ = SetPoints(layer.Name, curve.Points.RemoveAt(hit));
        }
        else if (hit >= 0)
        {
            _dragged = hit;
        }
        else if (!input.Modifiers.HasFlag(ToolModifiers.Control))
        {
            CurvePoint point = NewPoint(curve, x, z);
            int index = input.Modifiers.HasFlag(ToolModifiers.Shift) ? InsertAt(curve, x, z) : curve.Points.Length;
            _ = SetPoints(layer.Name, curve.Points.Insert(index, point));
        }
    }

    public void Drag(float x, float z, ToolInput input)
    {
        if (_dragged < 0 || Curve is not { Curve: HeightCurve } layer)
        {
            return;
        }
        using Performance.Span measure = Performance.Measure("tool.curve");
        if (_edit is null)
        {
            if (model.StrokeInProgress is not null)
            {
                _dragged = -1;
                return;
            }
            _edit = model.StartStroke($"Move a point of the curve {layer.Name}");
            _before = _edit.Document;
        }
        if (model.StrokeInProgress != _edit)
        {
            return;
        }
        MapDocument before = _before!;
        string name = layer.Name;
        HeightCurve original = HeightLayers.Find(before, name)!.Curve!;
        int index = _dragged;
        if (index >= original.Points.Length)
        {
            return;
        }
        HeightCurve moved = original with { Points = original.Points.SetItem(index, original.Points[index] with { X = x, Z = z }) };
        _edit.Apply(_ => HeightLayerEdits.SetCurve(before, name, moved));
        model.ShowStroke();
        OnPropertyChanged(nameof(Marks));
    }

    public void Release()
    {
        if (_edit is not null)
        {
            model.EndStroke(_edit);
        }
        _edit = null;
        _before = null;
        _dragged = -1;
        OnPropertyChanged(nameof(Marks));
    }

    public bool Key(string key, ToolModifiers modifiers)
    {
        switch (key)
        {
            case "Escape" when Curve is not null && !_startNew:
                _startNew = true;
                model.Status = "The next click starts another curve";
                return true;
            case "Back" or "Delete" when Curve is { Curve: HeightCurve curve } layer && !curve.Points.IsEmpty:
                _ = SetPoints(layer.Name, curve.Points.RemoveAt(curve.Points.Length - 1));
                return true;
            default:
                return false;
        }
    }

    private Task SetPoints(string layer, System.Collections.Immutable.ImmutableArray<CurvePoint> points) =>
        model.RunWithAsync("terrain.setCurve", new JsonObject
        {
            ["layer"] = layer,
            ["points"] = new JsonArray([.. points.Select(point => (JsonNode)new JsonArray(point.X, point.Z, point.Height))]),
        });

    /// <summary>
    /// A new point of a curve: a road's on the terrain, the others with the height of the curve's
    /// last point (the tool's for a curve without points).
    /// </summary>
    private CurvePoint NewPoint(HeightCurve curve, float x, float z)
    {
        float height = curve.Profile == CurveProfile.Road
            ? model.ShownDocument!.Terrain.Heightmap.HeightAt(x, z)
            : curve.Points.LastOrDefault()?.Height ?? (float)Height;
        return new CurvePoint(x, z, height);
    }

    private static int Nearest(HeightCurve curve, float x, float z, float reach)
    {
        int best = -1;
        float nearest = reach * reach;
        for (int i = 0; i < curve.Points.Length; i++)
        {
            float dx = curve.Points[i].X - x;
            float dz = curve.Points[i].Z - z;
            float distance = dx * dx + dz * dz;
            if (distance <= nearest)
            {
                nearest = distance;
                best = i;
            }
        }
        return best;
    }

    /// <summary>
    /// Where a point inserted at a place goes: after the point that starts the nearest part of the
    /// curve's polygon.
    /// </summary>
    private static int InsertAt(HeightCurve curve, float x, float z)
    {
        if (curve.Points.Length < 2)
        {
            return curve.Points.Length;
        }
        int best = curve.Points.Length;
        float nearest = float.MaxValue;
        int segments = curve.Closed ? curve.Points.Length : curve.Points.Length - 1;
        for (int i = 0; i < segments; i++)
        {
            CurvePoint a = curve.Points[i];
            CurvePoint b = curve.Points[(i + 1) % curve.Points.Length];
            float ax = b.X - a.X, az = b.Z - a.Z;
            float length = ax * ax + az * az;
            float t = length > 0 ? Math.Clamp(((x - a.X) * ax + (z - a.Z) * az) / length, 0, 1) : 0;
            float dx = a.X + ax * t - x, dz = a.Z + az * t - z;
            float distance = dx * dx + dz * dz;
            if (distance < nearest)
            {
                nearest = distance;
                best = i + 1;
            }
        }
        return best;
    }
}
