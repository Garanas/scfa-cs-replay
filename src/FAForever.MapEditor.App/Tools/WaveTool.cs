using System.Globalization;
using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tools;

/// <summary>
/// Places the water's wave generators with the mouse (user story W-1): a click places one on the
/// water's surface, its waves rolling the tool's <see cref="Heading"/>; dragging from the place
/// aims them the way the mouse goes. With the symmetry mirroring edits, its images are placed in
/// the other parts of the map in the same step. A press on a generator's square selects it (the
/// Water inspector shows its fields) and dragging moves it; Delete removes the selected one. Both
/// views draw every generator while the tool is active: a square and an arrow the way its waves roll.
/// </summary>
public sealed partial class WaveTool(MainViewModel model) : ObservableObject, IMapTool
{
    /// <summary>
    /// How far, in pixels, a press may be from a generator's square to take it.
    /// </summary>
    public const float ReachPixels = 8;

    /// <summary>
    /// How long, in pixels, the arrow of a generator is drawn.
    /// </summary>
    private const float ArrowPixels = 28;

    /// <summary>
    /// How far, in pixels, the mouse must go from the press before a drag aims the waves.
    /// </summary>
    private const float AimPixels = 4;

    private EditTransaction? _edit;
    private MapDocument? _before;
    private Vector2 _pressed;
    private float _ogridsPerPixel = 0.5f;
    private int? _moving;
    private int[] _placed = [];

    /// <summary>
    /// The compass heading of a click's waves, in degrees: 0 north, 90 east.
    /// </summary>
    [ObservableProperty]
    public partial double Heading { get; set; } = 180;

    /// <summary>
    /// How fast new waves roll, in ogrids a tick.
    /// </summary>
    [ObservableProperty]
    public partial double Speed { get; set; } = WaveGeneratorEdits.DefaultSpeed;

    public double CursorRadius => 0;

    public string Help => "Click on the water: a wave generator; drag from it: the way its waves roll. Drag a square to move it, Delete removes the selected one";

    /// <summary>
    /// The index of the generator the Water inspector shows, or null.
    /// </summary>
    public int? Selected => model.WaterSettings.SelectedGenerator?.Index;

    public IReadOnlyList<ToolShape> Shapes(ToolView view)
    {
        if (model.ShownDocument is not MapDocument document || document.Water.WaveGenerators.IsEmpty)
        {
            return [];
        }
        using Performance.Span measure = Performance.Measure("tool.wave-shapes");
        List<ToolShape> shapes = [];
        int? selected = Selected;
        for (int i = 0; i < document.Water.WaveGenerators.Length; i++)
        {
            WaveGenerator generator = document.Water.WaveGenerators[i];
            if (view.ToScreen(generator.Position) is not Vector2 at)
            {
                continue;
            }
            Vector2 direction = WaveGeneratorEdits.DirectionOf(generator);
            float reach = ArrowPixels * view.OgridsPerPixelAt(generator.Position);
            Vector3 tip3 = generator.Position + new Vector3(direction.X, 0, direction.Y) * reach;
            bool chosen = i == selected || i == _moving || _placed.Contains(i);
            if (view.ToScreen(tip3) is Vector2 tip && Vector2.DistanceSquared(at, tip) > 1)
            {
                Vector2 along = Vector2.Normalize(tip - at);
                Vector2 across = new Vector2(-along.Y, along.X);
                shapes.Add(new ToolLine([at, tip], false, 4, ToolInk.Shadow));
                shapes.Add(new ToolLine([at, tip], false, 2, chosen ? ToolInk.Hover : ToolInk.Selection));
                shapes.Add(new ToolTriangle(tip + along * 6, tip - along * 2 + across * 5, tip - along * 2 - across * 5, chosen ? ToolInk.Hover : ToolInk.Selection));
            }
            shapes.Add(new ToolSquare(at, 5, chosen ? ToolInk.Hover : ToolInk.Fill, ToolInk.Selection));
        }
        return shapes;
    }

    public void Press(float x, float z, ToolInput input)
    {
        _ogridsPerPixel = input.OgridsPerPixel;
        if (model.ShownDocument is MapDocument document && Near(document, x, z, input) is int index)
        {
            Select(index);
            StartMove(index);
            return;
        }
        Press(x, z);
    }

    /// <summary>
    /// The generator whose square lies under the mouse: within <see cref="ReachPixels"/> on screen
    /// when the view says where things show, else within that many pixels at the input's zoom.
    /// </summary>
    private static int? Near(MapDocument document, float x, float z, ToolInput input)
    {
        if (input.View is ToolView { Mouse: Vector2 mouse } view)
        {
            int? nearest = null;
            float best = ReachPixels * ReachPixels;
            for (int i = 0; i < document.Water.WaveGenerators.Length; i++)
            {
                if (view.ToScreen(document.Water.WaveGenerators[i].Position) is Vector2 at && Vector2.DistanceSquared(at, mouse) <= best)
                {
                    best = Vector2.DistanceSquared(at, mouse);
                    nearest = i;
                }
            }
            return nearest;
        }
        return document.WaveGeneratorNear(x, z, ReachPixels * input.OgridsPerPixel);
    }

    public void Press(float x, float z)
    {
        if (model.ShownDocument is null || model.StrokeInProgress is not null)
        {
            return;
        }
        _edit = model.StartStroke("Place a wave generator");
        _before = _edit.Document;
        _pressed = new Vector2(x, z);
        Place(WaveGeneratorEdits.DirectionOfHeading(Heading));
    }

    public void Drag(float x, float z, ToolInput input)
    {
        _ogridsPerPixel = input.OgridsPerPixel;
        Drag(x, z);
    }

    public void Drag(float x, float z)
    {
        if (_edit is null || model.StrokeInProgress != _edit || _before is not MapDocument before)
        {
            return;
        }
        if (_moving is int index)
        {
            _edit.Apply(_ => before.MoveWaveGenerator(index, x, z));
            model.ShowStroke();
            return;
        }
        Vector2 towards = new Vector2(x, z) - _pressed;
        if (towards.Length() >= AimPixels * _ogridsPerPixel)
        {
            Place(towards);
        }
    }

    public void Release()
    {
        if (_edit is null)
        {
            return;
        }
        bool placing = _moving is null;
        int[] placed = _placed;
        model.EndStroke(_edit);
        _edit = null;
        _before = null;
        _moving = null;
        _placed = [];
        if (placing && placed.Length > 0 && model.ShownDocument is MapDocument document)
        {
            Select(placed[0]);
            WaveGenerator generator = document.Water.WaveGenerators[placed[0]];
            model.Status = string.Create(CultureInfo.InvariantCulture,
                $"Placed {(placed.Length == 1 ? "a wave generator" : $"{placed.Length} wave generators")} at {generator.Position.X:0.#}, {generator.Position.Z:0.#}, heading {WaveGeneratorEdits.HeadingOf(WaveGeneratorEdits.DirectionOf(generator)):0}°");
        }
    }

    public bool Key(string key, ToolModifiers modifiers)
    {
        if (key is "Delete" or "Back" && Selected is int index && model.Map is not null && model.StrokeInProgress is null)
        {
            _ = model.RunWithAsync("water.removeWaveGenerator", new System.Text.Json.Nodes.JsonObject { ["index"] = index });
            return true;
        }
        return false;
    }

    /// <summary>
    /// The generator placed at the press, aimed <paramref name="direction"/>, with its images, from
    /// the document as it was at the press, so the step holds only them.
    /// </summary>
    private void Place(Vector2 direction)
    {
        if (_edit is null || _before is not MapDocument before)
        {
            return;
        }
        WaveGenerator generator = WaveGeneratorEdits.Aimed(
            AddWaveGeneratorHandler.Default(new Vector3(_pressed.X, before.Water.Elevation, _pressed.Y)), direction, (float)Speed);
        int[] placed = [];
        _edit.Apply(_ => before.AddWaveGenerators(generator, model.EditTransforms(before), out placed));
        _placed = placed;
        model.ShowStroke();
    }

    private void StartMove(int index)
    {
        if (model.StrokeInProgress is not null)
        {
            return;
        }
        _edit = model.StartStroke($"Move wave generator {index + 1}");
        _before = _edit.Document;
        _moving = index;
    }

    private void Select(int index)
    {
        if (index < model.WaterSettings.Generators.Count)
        {
            model.WaterSettings.SelectedGenerator = model.WaterSettings.Generators[index];
        }
    }
}
