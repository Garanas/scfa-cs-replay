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

    /// <summary>
    /// The choices of where a new point's height comes from, as the tool's option shows them.
    /// </summary>
    public static IReadOnlyList<string> PointHeightChoices { get; } = ["Automatic", "Last point's", "Ground", "Typed"];

    /// <summary>
    /// Where a new point's height comes from (see <see cref="PointHeightChoices"/>): automatic is the
    /// ground for a road (a ramp follows the land) and the last point's for the other profiles; the
    /// others are the last point's height, the ground under the click and <see cref="TypedHeight"/>.
    /// </summary>
    [ObservableProperty]
    public partial string PointHeightChoice { get; set; } = PointHeightChoices[0];

    /// <summary>
    /// The height a new point gets when <see cref="PointHeightChoice"/> is Typed.
    /// </summary>
    [ObservableProperty]
    public partial double TypedHeight { get; set; } = 10;

    /// <summary>
    /// Whether the views draw the lines of the chosen curve's width (dashed) and fade (dotted).
    /// </summary>
    [ObservableProperty]
    public partial bool ShowReach { get; set; } = true;

    /// <summary>
    /// Whether the typed height's field shows.
    /// </summary>
    public bool IsTypedHeight => PointHeightChoice == PointHeightChoices[3];

    partial void OnPointHeightChoiceChanged(string value) => OnPropertyChanged(nameof(IsTypedHeight));

    partial void OnShowReachChanged(bool value) => OnPropertyChanged(nameof(Marks));

    /// <summary>
    /// The chosen points of the curve (the table's rows, the points pressed with the tool); the
    /// views draw them larger.
    /// </summary>
    public CurveNodesViewModel Nodes => model.HeightStack.Nodes;

    public string Help => "Click to add points to the chosen curve (or start a new one); shift and a click inserts a point, drag a point to move it, shift and drag a point sets its height, + and - change the chosen point's height, control and a click removes a point; Escape starts another curve";

    /// <summary>
    /// The chosen curve when it can be edited: a curve layer, not locked; null otherwise.
    /// </summary>
    public Layer? Curve =>
        model.ShownDocument is MapDocument document && HeightLayers.Find(document, model.HeightStack.Chosen) is { Source: CurveSource, Locked: false } layer
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
            // the views ask every frame: made again only for another curve, size, chosen points or option
            IReadOnlyList<int> chosen = Nodes.Chosen;
            MapDocument shown = model.ShownDocument!;
            if (_marks is { } made && ReferenceEquals(made.Curve, curve) && ReferenceEquals(made.Chosen, chosen) && made.ShowReach == ShowReach
                && made.Width == shown.Width && made.Height == shown.Height)
            {
                return made.Marks;
            }
            using Performance.Span measure = Performance.Measure("tool.curve-marks");
            IReadOnlyList<CurvePoint> line = HeightCurves.Spline(curve);
            // a curve mirrored by the symmetry shows its images too (F-12)
            IReadOnlyList<IReadOnlyList<(float X, float Z)>>? images = curve.Symmetry is not null
                ? [.. HeightCurves.Parts(curve, shown.Width, shown.Height).Skip(1).Select(part => (IReadOnlyList<(float X, float Z)>)[.. HeightCurves.Spline(part).Select(point => (point.X, point.Z))])]
                : null;
            // how far it reaches: the width's edge dashed, the fade's end dotted
            IReadOnlyList<MarkOutline>? outlines = ShowReach
                ? [.. CurveOutlines.Of(curve, shown.Width, shown.Height).Select(outline => new MarkOutline([.. outline.Points.Select(point => (point.X, point.Y))], outline.Kind == CurveOutlineKind.Fade, outline.Part > 0))]
                : null;
            ToolMarks marks = new ToolMarks([.. curve.Points.Select(point => (point.X, point.Z))], Path: [.. line.Select(point => (point.X, point.Z))], Images: images,
                Outlines: outlines, ChosenHandles: chosen, CornerHandles: [.. Enumerable.Range(0, curve.Points.Length).Where(index => curve.Points[index].Sharp)]);
            _marks = (curve, chosen, ShowReach, shown.Width, shown.Height, marks);
            return marks;
        }
    }

    private (HeightCurve Curve, IReadOnlyList<int> Chosen, bool ShowReach, int Width, int Height, ToolMarks Marks)? _marks;

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
        Layer? layer = _startNew ? null : Curve;
        if (layer?.Curve is not HeightCurve curve)
        {
            _startNew = false;
            // a first point has a height of its own only when the option says so (a road's lie on the ground)
            JsonArray first = PointHeightChoice is "Ground" or "Typed" ? new JsonArray(x, z, NewHeight(Profile, null, x, z)) : new JsonArray(x, z);
            _ = model.RunWithAsync("terrain.addCurve", new JsonObject
            {
                ["points"] = new JsonArray(first),
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
            // the point pressed is the chosen one (the table highlights its row, the views draw it larger)
            _dragged = hit;
            Nodes.Choose([hit]);
            // shift and a drag sets its height; the mouse's height at the start is what the drag measures from
            _heightDrag = input.Modifiers.HasFlag(ToolModifiers.Shift);
            if (_heightDrag)
            {
                if (curve.Fills)
                {
                    model.Status = "A closed curve that fills has one height: set it in the curve's settings";
                    _dragged = -1;
                    _heightDrag = false;
                    return;
                }
                _pressedY = input.ViewAt(x, z).Mouse?.Y ?? 0;
                _startHeight = curve.Points[hit].Height;
            }
        }
        else if (!input.Modifiers.HasFlag(ToolModifiers.Control))
        {
            CurvePoint point = NewPoint(curve, x, z);
            int index = input.Modifiers.HasFlag(ToolModifiers.Shift) ? InsertAt(curve, x, z) : curve.Points.Length;
            _ = AddPoint(layer.Name, curve.Points.Insert(index, point), index);
        }
    }

    private bool _heightDrag;
    private float _pressedY;
    private float _startHeight;

    /// <summary>
    /// How many height units one pixel of the mouse's height drag is worth, per ogrid a pixel covers.
    /// </summary>
    private const float UnitsPerOgrid = 0.5f;

    /// <summary>
    /// Sets the points, then chooses the one added.
    /// </summary>
    private async Task AddPoint(string layer, System.Collections.Immutable.ImmutableArray<CurvePoint> points, int index)
    {
        await SetPoints(layer, points);
        Nodes.Choose([index]);
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
            _edit = model.StartStroke(_heightDrag ? $"Set the height of a point of the curve {layer.Name}" : $"Move a point of the curve {layer.Name}");
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
        CurvePoint changed = original.Points[index] with { X = x, Z = z };
        if (_heightDrag)
        {
            // up on the screen is higher, in both views: the mouse's height on the screen, whatever the ground under it
            float mouseY = input.ViewAt(x, z).Mouse?.Y ?? _pressedY;
            float height = MathF.Round(_startHeight + (_pressedY - mouseY) * input.OgridsPerPixel * UnitsPerOgrid, 1);
            changed = original.Points[index] with { Height = height };
            model.Status = $"Point {index} of {name}: height {height.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}";
        }
        HeightCurve moved = original with { Points = original.Points.SetItem(index, changed) };
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
        _heightDrag = false;
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
            case "OemPlus" or "Add" or "OemMinus" or "Subtract" when Curve is { Curve: HeightCurve chosenCurve } chosenLayer:
                return ChangeHeights(chosenLayer.Name, chosenCurve, key is "OemPlus" or "Add" ? 1 : -1, modifiers);
            case "Back" or "Delete" when Curve is { Curve: HeightCurve curve } layer && !curve.Points.IsEmpty:
                _ = SetPoints(layer.Name, curve.Points.RemoveAt(curve.Points.Length - 1));
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// + and - change the chosen points' heights by 1 (shift: 0.1, control: 5), one step each.
    /// </summary>
    private bool ChangeHeights(string layer, HeightCurve curve, int direction, ToolModifiers modifiers)
    {
        if (curve.Fills)
        {
            model.Status = "A closed curve that fills has one height: set it in the curve's settings";
            return true;
        }
        int[] chosen = [.. Nodes.Chosen.Where(index => index < curve.Points.Length)];
        if (chosen.Length == 0)
        {
            model.Status = "Choose a point first: click it, or its row in the inspector";
            return true;
        }
        float step = direction * (modifiers.HasFlag(ToolModifiers.Control) ? 5f : modifiers.HasFlag(ToolModifiers.Shift) ? 0.1f : 1f);
        System.Collections.Immutable.ImmutableArray<CurvePoint> points = curve.Points;
        foreach (int index in chosen)
        {
            points = points.SetItem(index, points[index] with { Height = MathF.Round(points[index].Height + step, 2) });
        }
        model.Status = $"Point {chosen[^1]} of {layer}: height {points[chosen[^1]].Height.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture)}";
        // one point: its own command, whose steps merge while + is pressed again
        _ = chosen.Length == 1
            ? model.RunWithAsync("terrain.setCurvePoint", new JsonObject { ["layer"] = layer, ["index"] = chosen[0], ["height"] = points[chosen[0]].Height })
            : SetPoints(layer, points);
        return true;
    }

    private Task SetPoints(string layer, System.Collections.Immutable.ImmutableArray<CurvePoint> points) =>
        model.RunWithAsync("terrain.setCurve", new JsonObject
        {
            ["layer"] = layer,
            // a point with values of its own keeps them
            ["points"] = new JsonArray([.. points.Select(Commands.CurvePointArgument.ToJson)]),
        });

    /// <summary>
    /// A new point of a curve: a road's on the terrain, the others with the height of the curve's
    /// last point (the tool's for a curve without points).
    /// </summary>
    private CurvePoint NewPoint(HeightCurve curve, float x, float z)
    {
        return new CurvePoint(x, z, NewHeight(curve.Profile, curve.Points.LastOrDefault()?.Height, x, z));
    }

    /// <summary>
    /// The height a new point gets by <see cref="PointHeightChoice"/>.
    /// </summary>
    private float NewHeight(CurveProfile profile, float? last, float x, float z)
    {
        float Ground() => model.ShownHeightmap?.HeightAt(x, z) ?? 0;
        float Last() => last ?? (float)Height;
        return PointHeightChoices.ToList().IndexOf(PointHeightChoice) switch
        {
            1 => Last(),
            2 => Ground(),
            3 => (float)TypedHeight,
            _ => profile == CurveProfile.Road ? Ground() : Last(),
        };
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
