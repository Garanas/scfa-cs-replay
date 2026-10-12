using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tools;

/// <summary>
/// The Sketch mode's eraser (user story N-1): rubs out the parts of the chosen sheet's strokes it
/// passes over (a stroke is cut in two where the eraser crosses its middle); with Alt held, or Whole
/// strokes on, it removes the strokes it touches entirely. With Mirror it also erases where the
/// map's symmetry repeats it. The strokes go while the mouse is dragged (a transaction, as a brush's
/// stroke is) and the whole drag is one step of undo.
/// </summary>
public sealed partial class EraserTool : ObservableObject, IMapTool
{
    private readonly MainViewModel model;

    public EraserTool(MainViewModel model)
    {
        this.model = model;
        model.Sketch.ChosenChanged += (_, _) => OnPropertyChanged(nameof(SheetText));
    }

    /// <summary>
    /// The least and greatest radius, in ogrids.
    /// </summary>
    public const double MinimumRadius = 1, MaximumRadius = 64;

    private EditTransaction? _edit;
    private (float X, float Z) _last;
    private bool _whole;
    private int _erased;
    private string _sheet = "";

    /// <summary>
    /// How far the eraser reaches, in ogrids.
    /// </summary>
    [ObservableProperty]
    public partial double Radius { get; set; } = 5;

    /// <summary>
    /// Whether the eraser also erases where the map's symmetry repeats it (while it mirrors edits).
    /// </summary>
    [ObservableProperty]
    public partial bool Mirror { get; set; } = true;

    /// <summary>
    /// Whether it removes whole strokes without Alt held.
    /// </summary>
    [ObservableProperty]
    public partial bool Whole { get; set; }

    public double CursorRadius => Radius;

    public bool UsesAlt => true;

    public bool Mirrors => Mirror;

    public string SheetText => model.Sketch.Chosen is string sheet ? $"Sheet: {sheet}" : "No sheet yet";

    public void Press(float x, float z) => Press(x, z, ToolInput.Default);

    public void Press(float x, float z, ToolInput input)
    {
        if (model.ShownDocument is null)
        {
            return;
        }
        if (input.View is { IsTopDown: false })
        {
            model.Status = "Erase in the top view: the 3D view shows no sketch yet";
            return;
        }
        _sheet = model.Sketch.Chosen ?? "";
        _whole = Whole || input.Modifiers.HasFlag(ToolModifiers.Alt);
        _erased = 0;
        _last = (x, z);
        _edit = model.StartStroke($"Erase in the sketch{(_whole ? " (whole strokes)" : "")}");
        Erase([new Vector2(x, z)]);
        model.ShowStroke();
    }

    public void Drag(float x, float z) => Drag(x, z, ToolInput.Default);

    public void Drag(float x, float z, ToolInput input)
    {
        if (_edit is null || model.StrokeInProgress != _edit)
        {
            return;
        }
        using Performance.Span measure = Performance.Measure("tool.sketch-eraser");
        _whole = Whole || input.Modifiers.HasFlag(ToolModifiers.Alt);
        Vector2 from = new Vector2(_last.X, _last.Z);
        Vector2 to = new Vector2(x, z);
        if (from == to)
        {
            return;
        }
        Erase([from, to]);
        _last = (x, z);
        model.ShowStroke();
    }

    public void Release()
    {
        if (_edit is not EditTransaction edit)
        {
            return;
        }
        _edit = null;
        model.EndStroke(edit);
        if (_erased > 0)
        {
            model.Status = $"Erased {_erased} {(_erased == 1 ? "item" : "items")} of {_sheet}";
        }
    }

    public bool Key(string key, ToolModifiers modifiers)
    {
        if (key == "Escape" && _edit is EditTransaction edit)
        {
            _edit = null;
            model.EndStroke(edit, keep: false);
            model.Status = "Cancelled the erasing";
            return true;
        }
        return false;
    }

    /// <summary>
    /// <c>[</c> and <c>]</c>: a fifth smaller or larger, within the range.
    /// </summary>
    public double StepRadius(int steps) => Radius = BrushKeys.RadiusStep(Radius, steps, MaximumRadius);

    private void Erase(IReadOnlyList<Vector2> path)
    {
        if (_edit is null || _sheet.Length == 0 || model.ShownDocument is not MapDocument document || document.Sketch.Find(_sheet) is null)
        {
            return;
        }
        IReadOnlyList<MapSymmetryTransform> parts = Mirror ? model.EditTransforms(document) : [];
        int erased = 0;
        _edit.Apply(d => d.Erase(_sheet, path, (float)Radius, _whole, parts, out erased));
        _erased += erased;
    }
}
