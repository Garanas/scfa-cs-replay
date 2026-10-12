using System.Numerics;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tools;

/// <summary>
/// The Sketch mode's pencil (user story N-1): drag to draw a stroke in the colour and width in hand
/// into the chosen sheet; keys 1 to 6 pick the colour and <c>[</c> and <c>]</c> the width. With Mirror
/// the stroke is repeated by the map's symmetry, as the brushes' dabs are; with Smooth the line is
/// smoothed and thinned to few places; with Shift held it is a straight line from where it began.
/// The line is shown while the mouse is down (<see cref="SketchViewModel.Pending"/>) and becomes
/// one stroke, one step of undo, when it is released: the command <c>sketch.draw</c> makes it.
/// </summary>
public sealed partial class PencilTool : ObservableObject, IMapTool
{
    private readonly MainViewModel _model;
    private readonly List<Vector2> _points = [];
    private bool _drawing;
    private bool _straight;
    private float _pixel = 0.5f;

    public PencilTool(MainViewModel model)
    {
        _model = model;
        Colours = [.. Enum.GetValues<SketchColour>().Select(colour => new SketchColourChoice(colour, ChooseColour))];
        Widths = [.. Enum.GetValues<SketchWidth>().Select(width => new SketchWidthChoice(width, ChooseWidth))];
        Colours[(int)Colour].IsOn = true;
        Widths[(int)Width].IsOn = true;
        model.Sketch.ChosenChanged += (_, _) => OnPropertyChanged(nameof(SheetText));
    }

    /// <summary>
    /// The six pencils, in the order of the keys 1 to 6.
    /// </summary>
    public IReadOnlyList<SketchColourChoice> Colours { get; }

    public IReadOnlyList<SketchWidthChoice> Widths { get; }

    /// <summary>
    /// The colour in hand.
    /// </summary>
    [ObservableProperty]
    public partial SketchColour Colour { get; set; } = SketchColour.Red;

    /// <summary>
    /// How thick the strokes are drawn.
    /// </summary>
    [ObservableProperty]
    public partial SketchWidth Width { get; set; } = SketchWidth.Medium;

    /// <summary>
    /// Whether a stroke is repeated by the map's symmetry (while it mirrors edits).
    /// </summary>
    [ObservableProperty]
    public partial bool Mirror { get; set; } = true;

    /// <summary>
    /// Whether the line is smoothed and thinned to few places when it is released.
    /// </summary>
    [ObservableProperty]
    public partial bool Smooth { get; set; }

    partial void OnColourChanged(SketchColour value)
    {
        foreach (SketchColourChoice choice in Colours)
        {
            choice.IsOn = choice.Colour == value;
        }
    }

    partial void OnWidthChanged(SketchWidth value)
    {
        foreach (SketchWidthChoice choice in Widths)
        {
            choice.IsOn = choice.Width == value;
        }
    }

    private Task ChooseColour(SketchColour colour) => _model.RunWithAsync("sketch.setPencil", new JsonObject { ["colour"] = colour.ToString() });

    private Task ChooseWidth(SketchWidth width) => _model.RunWithAsync("sketch.setPencil", new JsonObject { ["width"] = width.ToString() });

    /// <summary>
    /// What the options row says of the sheet drawn into.
    /// </summary>
    public string SheetText => _model.Sketch.Chosen is string sheet ? $"Sheet: {sheet}" : "No sheet yet: the first stroke makes one";

    public double CursorRadius => 0;

    /// <summary>
    /// Whether a stroke is being drawn (the mouse is down).
    /// </summary>
    public bool IsDrawing => _drawing;

    public void Press(float x, float z) => Press(x, z, ToolInput.Default);

    public void Press(float x, float z, ToolInput input)
    {
        if (_model.ShownDocument is null)
        {
            return;
        }
        _points.Clear();
        _points.Add(new Vector2(x, z));
        _drawing = true;
        _pixel = input.OgridsPerPixel;
        _straight = input.Modifiers.HasFlag(ToolModifiers.Shift);
        ShowLine();
    }

    public void Drag(float x, float z) => Drag(x, z, ToolInput.Default);

    public void Drag(float x, float z, ToolInput input)
    {
        if (!_drawing)
        {
            return;
        }
        using Performance.Span measure = Performance.Measure("tool.sketch-pencil");
        _pixel = input.OgridsPerPixel;
        _straight = input.Modifiers.HasFlag(ToolModifiers.Shift);
        Vector2 at = new Vector2(x, z);
        // a place closer than half a pixel to the last adds nothing to the line
        if (Vector2.DistanceSquared(at, _points[^1]) >= _pixel * _pixel * 0.25f || _points.Count == 1)
        {
            _points.Add(at);
        }
        else
        {
            _points[^1] = at;
        }
        ShowLine();
    }

    public void Release()
    {
        if (!_drawing)
        {
            return;
        }
        _drawing = false;
        JsonArray points = new JsonArray([.. _points.Select(point => (JsonNode)new JsonArray(Math.Round(point.X, 2), Math.Round(point.Y, 2)))]);
        JsonObject arguments = new JsonObject
        {
            ["points"] = points,
            ["colour"] = Colour.ToString(),
            ["width"] = Width.ToString(),
            ["mirror"] = Mirror,
            ["smooth"] = Smooth,
            ["straight"] = _straight,
            ["tolerance"] = Math.Round(_pixel, 3),
        };
        _points.Clear();
        // the command makes the stroke; the line shown meanwhile goes when it is part of the sketch
        Task ran = _model.RunWithAsync("sketch.draw", arguments);
        if (ran.IsCompleted)
        {
            _model.Sketch.SetPending([]);
        }
        else
        {
            SynchronizationContext? ui = SynchronizationContext.Current;
            _ = ran.ContinueWith(_ => UiThread.Run(ui, () => _model.Sketch.SetPending([])), TaskScheduler.Default);
        }
    }

    public bool Key(string key, ToolModifiers modifiers)
    {
        if (key == "Escape" && _drawing)
        {
            _drawing = false;
            _points.Clear();
            _model.Sketch.SetPending([]);
            _model.Status = "Cancelled the stroke";
            return true;
        }
        if (modifiers != ToolModifiers.None)
        {
            return false;
        }
        // 1 to 6 pick the colour
        string digit = key.Length == 2 && key[0] == 'D' ? key[1..] : key.StartsWith("NumPad", StringComparison.Ordinal) && key.Length == 7 ? key[6..] : "";
        if (digit.Length == 1 && digit[0] is >= '1' and <= '6')
        {
            _ = _model.RunWithAsync("sketch.setPencil", new JsonObject { ["colour"] = (int)(digit[0] - '0') });
            return true;
        }
        return false;
    }

    /// <summary>
    /// <c>[</c> and <c>]</c>: a step thinner or thicker, within the three widths.
    /// </summary>
    /// <returns>The width now.</returns>
    public SketchWidth StepWidth(int steps)
    {
        Width = (SketchWidth)Math.Clamp((int)Width + Math.Sign(steps), 0, Enum.GetValues<SketchWidth>().Length - 1);
        return Width;
    }

    // the line drawn so far, with its images by the symmetry, for the views
    private void ShowLine()
    {
        if (_model.ShownDocument is not MapDocument document)
        {
            return;
        }
        IReadOnlyList<Vector2> line = _straight ? SketchGeometry.Straight(_points) : _points;
        SketchStroke stroke = new SketchStroke(Colour, Width, [.. line]);
        IReadOnlyList<MapSymmetryTransform> parts = Mirror ? _model.EditTransforms(document) : [];
        _model.Sketch.SetPending(SketchEdits.WithImages(stroke, parts));
    }
}
