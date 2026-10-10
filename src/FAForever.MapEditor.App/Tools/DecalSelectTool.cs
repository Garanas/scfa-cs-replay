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
/// What dragging a selected decal does with the Decals mode's select tool.
/// </summary>
public enum DecalDrag
{
    /// <summary>
    /// Moves the selection.
    /// </summary>
    Move,

    /// <summary>
    /// Scales each selected decal about its centre, both sides alike.
    /// </summary>
    Scale,

    /// <summary>
    /// Scales only the width (along each decal's own x axis).
    /// </summary>
    Width,

    /// <summary>
    /// Scales only the length (along each decal's own z axis).
    /// </summary>
    Length,
}

/// <summary>
/// The Decals mode's select tool (user stories F-11, D-1): the select tool for decals
/// (<see cref="SelectTool"/>: click, shift, control, a box, drag to move, Q and E turn, the arrows
/// nudge, Delete), with scaling. <see cref="DragMode"/> (the options, or S to go to the next) makes
/// dragging a selected decal scale the selection, both sides or one; the further the mouse goes
/// from the selection's middle, the larger. Plus and minus scale by a tenth (with shift a hundredth).
/// </summary>
public sealed partial class DecalSelectTool : ObservableObject, IMapTool
{
    private const float DragPixels = 3;

    private readonly MainViewModel _model;
    private readonly SelectTool _select;
    private bool _scaling;
    private bool _dragging;
    private (float X, float Z) _from;
    private Vector2 _centre;
    private float _reach;
    private ToolInput _input = ToolInput.Default;
    private EditTransaction? _scale;
    private MapDocument? _before;
    private ImmutableArray<ObjectRef> _scaled = [];

    public DecalSelectTool(MainViewModel model)
    {
        _model = model;
        _select = new SelectTool(model, ObjectKinds.Decals);
    }

    /// <summary>
    /// What dragging a selected decal does.
    /// </summary>
    [ObservableProperty]
    public partial DecalDrag DragMode { get; set; }

    public IReadOnlyList<DecalDrag> DragModes { get; } = Enum.GetValues<DecalDrag>();

    public double CursorRadius => 0;

    public (float X0, float Z0, float X1, float Z1)? Box => _select.Box;

    /// <summary>
    /// The select tool it wraps, which holds the handles of the selection.
    /// </summary>
    public SelectTool Select => _select;

    public IReadOnlyList<ToolShape> Shapes(ToolView view) => _select.Shapes(view);

    /// <summary>
    /// What the options bar says about using it.
    /// </summary>
    public string Help => "Click to select, shift adds, control toggles; drag a box; drag the selection to move or scale it (S: the next drag mode); Q and E turn, plus and minus scale, the arrows move, Delete removes";

    public void Press(float x, float z) => Press(x, z, ToolInput.Default);

    public void Drag(float x, float z) => Drag(x, z, _input);

    public void Press(float x, float z, ToolInput input)
    {
        _input = input;
        _scaling = false;
        _dragging = false;
        // the handles before what lies under them
        // (in a scale drag mode the decal under the middle square scales, as without handles)
        if (_select.HandleAt(x, z, input) is { IsNone: false } part && !(part.Kind == HandleKind.Move && DragMode != DecalDrag.Move))
        {
            _select.Press(x, z, input);
            return;
        }
        if (DragMode != DecalDrag.Move && _model.ShownDocument is MapDocument document)
        {
            ObjectRef? under = input.Picks
                ? input.Picked is ObjectRef found && found.Kind == ObjectKind.Decal && !_model.Decals.IsHidden(found) ? found : null
                : _model.PickAt(x, z, input.OgridsPerPixel, ObjectKinds.Decals & _model.SelectableKinds);
            if (under is ObjectRef picked && !input.Modifiers.HasFlag(ToolModifiers.Control))
            {
                if (!_model.Selection.Contains(picked))
                {
                    _model.Selection.Apply([picked], input.Modifiers.HasFlag(ToolModifiers.Shift) ? SelectionChange.Add : SelectionChange.Replace);
                }
                _scaling = true;
                _from = (x, z);
                Vector3[] centres = [.. _model.Selection.Items.Where(thing => thing.Kind == ObjectKind.Decal).Select(document.PlaceOf).OfType<Vector3>()];
                _centre = new Vector2(centres.Average(c => c.X), centres.Average(c => c.Z));
                // pressed on the middle: a short way from it counts as the start
                _reach = MathF.Max(Vector2.Distance(_centre, new Vector2(x, z)), 4 * input.OgridsPerPixel);
                return;
            }
        }
        _select.Press(x, z, input);
    }

    public void Drag(float x, float z, ToolInput input)
    {
        if (!_scaling)
        {
            _select.Drag(x, z, input);
            return;
        }
        if (!_dragging)
        {
            if (MathF.Sqrt((x - _from.X) * (x - _from.X) + (z - _from.Z) * (z - _from.Z)) < DragPixels * _input.OgridsPerPixel)
            {
                return;
            }
            _dragging = true;
        }
        ScaleTo(x, z);
    }

    public void Release()
    {
        if (!_scaling)
        {
            _select.Release();
            return;
        }
        if (_scale is not null)
        {
            _model.EndStroke(_scale);
        }
        _scaling = false;
        _scale = null;
        _before = null;
        _scaled = [];
    }

    public bool Key(string key, ToolModifiers modifiers)
    {
        if (modifiers.HasFlag(ToolModifiers.Control) || modifiers.HasFlag(ToolModifiers.Alt))
        {
            return _select.Key(key, modifiers);
        }
        if (key == "S")
        {
            DragMode = (DecalDrag)(((int)DragMode + 1) % DragModes.Count);
            _model.Status = $"Dragging the selection: {Describe(DragMode)}";
            return true;
        }
        float step = modifiers.HasFlag(ToolModifiers.Shift) ? 0.01f : 0.1f;
        float? factor = key switch
        {
            "OemPlus" or "Add" => 1 + step,
            "OemMinus" or "Subtract" => 1 / (1 + step),
            _ => null,
        };
        if (factor is float f && _model.Selection.Items.Any(thing => thing.Kind == ObjectKind.Decal))
        {
            JsonObject arguments = DragMode switch
            {
                DecalDrag.Width => new JsonObject { ["x"] = f },
                DecalDrag.Length => new JsonObject { ["z"] = f },
                _ => new JsonObject { ["factor"] = f },
            };
            _ = _model.RunWithAsync("decals.scale", arguments);
            return true;
        }
        return _select.Key(key, modifiers);
    }

    /// <summary>
    /// What a drag mode does, in a few words: <c>scale the width</c>.
    /// </summary>
    public static string Describe(DecalDrag mode) => mode switch
    {
        DecalDrag.Scale => "scale",
        DecalDrag.Width => "scale the width",
        DecalDrag.Length => "scale the length",
        _ => "move",
    };

    /// <summary>
    /// Scales the selected decals by how much further the mouse is from their middle than at the
    /// press: from the document as it was then, so the step of undo holds the whole drag.
    /// </summary>
    private void ScaleTo(float x, float z)
    {
        using Performance.Span measure = Performance.Measure("tool.scale-decals");
        if (_scale is null)
        {
            if (_model.Selection.IsEmpty || _model.StrokeInProgress is not null)
            {
                _scaling = false;
                return;
            }
            _scale = _model.StartStroke("Scale the decals");
            _before = _scale.Document;
            // with the symmetry, the mirror partners as they were at the press scale along (F-12)
            ImmutableArray<ObjectRef> decals = [.. _model.Selection.Items.Where(thing => thing.Kind == ObjectKind.Decal)];
            _scaled = [.. decals, .. _model.PartnersOf(_before, decals).Select(partner => partner.Partner)];
        }
        if (_model.StrokeInProgress != _scale)
        {
            return;
        }
        float factor = MathF.Max(Vector2.Distance(_centre, new Vector2(x, z)) / _reach, 0.01f);
        (float fx, float fz) = DragMode switch
        {
            DecalDrag.Width => (factor, 1f),
            DecalDrag.Length => (1f, factor),
            _ => (factor, factor),
        };
        MapDocument before = _before!;
        ImmutableArray<ObjectRef> scaled = _scaled;
        _scale.Apply(_ => before.ScaleDecals(scaled, fx, fz));
        _model.ShowStroke();
    }
}
