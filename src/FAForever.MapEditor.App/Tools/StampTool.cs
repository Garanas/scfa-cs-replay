using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tools;

/// <summary>
/// Presses a height stamp into the terrain where one clicks (user story T-2): a picture of the
/// workspace's brushes folder, turned (Q and E, or the options' rotation) and scaled to its size,
/// raising, lowering or setting the heights by its values, one step of undo a click, in every part
/// of the map with the symmetry. Shift and a drag take the part of the map in the box as a new stamp
/// (<c>terrain.captureStamp</c>): a 16-bit picture in the brushes folder's <c>stamps</c>, chosen at
/// once with the size, height and base that put that part back as it was.
/// </summary>
public sealed partial class StampTool : ObservableObject, IShapedBrushTool
{
    /// <summary>
    /// The largest half side of a stamp, in ogrids: a quarter of the largest map.
    /// </summary>
    public const double MaximumRadius = 512;

    private readonly MainViewModel _model;
    private BrushStroke? _stroke;
    private (float X, float Z)? _captureFrom;

    public StampTool(MainViewModel model)
    {
        _model = model;
        // a stamp is a picture: it has no round shape
        Shape = new BrushShapeSettings(model, pictureOnly: true);
    }

    public BrushShapeSettings Shape { get; }

    public IReadOnlyList<HeightStampMode> Modes { get; } = Enum.GetValues<HeightStampMode>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSetting))]
    public partial HeightStampMode Mode { get; set; } = HeightStampMode.Raise;

    /// <summary>
    /// Whether it sets the heights: only then does the base matter.
    /// </summary>
    public bool IsSetting => Mode == HeightStampMode.Set;

    /// <summary>
    /// Half the side of the stamp's square, in ogrids.
    /// </summary>
    [ObservableProperty]
    public partial double Radius { get; set; } = 32;

    /// <summary>
    /// The height of the picture's white, in game units.
    /// </summary>
    [ObservableProperty]
    public partial double Height { get; set; } = 20;

    /// <summary>
    /// Whether the base of <see cref="HeightStampMode.Set"/> is the ground under the click; else
    /// <see cref="Base"/>.
    /// </summary>
    [ObservableProperty]
    public partial bool BaseFromGround { get; set; } = true;

    /// <summary>
    /// The height of the picture's black when it sets the heights, in game units.
    /// </summary>
    [ObservableProperty]
    public partial double Base { get; set; } = 20;

    /// <summary>
    /// How much of the picture fades out towards its sides, as a part of half its side.
    /// </summary>
    [ObservableProperty]
    public partial double Edge { get; set; } = 0.15;

    /// <summary>
    /// The box being dragged to take a stamp from the map (shift and a drag).
    /// </summary>
    [ObservableProperty]
    public partial (float X0, float Z0, float X1, float Z1)? Box { get; private set; }

    public double CursorRadius => Radius;

    public string Help => "Click to press the stamp; Q and E turn it, [ and ] size it; shift and a drag take that part of the map as a stamp";

    /// <summary>
    /// Its settings for automation's <c>state</c>.
    /// </summary>
    public JsonObject Describe() => new JsonObject
    {
        ["stamp"] = Shape.Chosen.Key,
        ["mode"] = Mode.ToString(),
        ["radius"] = Radius,
        ["height"] = Height,
        ["baseFromGround"] = BaseFromGround,
        ["base"] = Base,
        ["edge"] = Edge,
        ["rotation"] = Shape.Rotation,
        ["capturing"] = Box is not null,
    };

    public void Press(float x, float z) => Press(x, z, ToolInput.Default);

    public void Press(float x, float z, ToolInput input)
    {
        if (_model.ShownDocument is not MapDocument document)
        {
            return;
        }
        if (input.Modifiers.HasFlag(ToolModifiers.Shift))
        {
            _captureFrom = (x, z);
            Box = (x, z, x, z);
            return;
        }
        if (!Shape.IsShaped)
        {
            _model.Status = "Choose the stamp's picture: a brush in the options or on the asset shelf, or shift and a drag to take one from the map";
            return;
        }
        if (BrushShapeSettings.TryRead(_model, Shape) is not (true, BrushShape shape))
        {
            return;
        }
        if (HeightLayers.SculptProblem(document, _model.HeightStack.Chosen) is string problem)
        {
            _model.Status = problem;
            return;
        }
        HeightStamp stamp = new HeightStamp
        {
            Mode = Mode,
            Shape = shape,
            Radius = (float)Radius,
            Height = (float)Height,
            Base = BaseFromGround ? document.Terrain.Heightmap.HeightAt(x, z) : (float)Base,
            Edge = (float)Math.Clamp(Edge, 0, 0.5),
        };
        float scale = document.Terrain.Heightmap.Scale;
        float rotation = Shape.NextRotation();
        string description = Mode switch
        {
            HeightStampMode.Raise => "Stamp: raise terrain",
            HeightStampMode.Lower => "Stamp: lower terrain",
            _ => "Stamp: set terrain",
        };
        // one dab in every part of the map, as one step; nothing more follows the mouse
        _stroke = new BrushStroke(_model, description, Radius, x, z,
            (edit, dab) => (stamp with { Rotation = dab.Rotation, Mirrored = dab.Mirrored }).Apply(edit.Heightmap, scale, dab.X, dab.Z),
            () => rotation);
        _model.Status = $"Pressed the stamp {Shape.Chosen.Title}";
    }

    public void Drag(float x, float z)
    {
        if (_captureFrom is (float x0, float z0))
        {
            Box = (x0, z0, x, z);
        }
    }

    public void Release()
    {
        _stroke?.End();
        _stroke = null;
        if (Box is (float x0, float z0, float x1, float z1))
        {
            Box = null;
            _captureFrom = null;
            _ = _model.RunWithAsync("terrain.captureStamp", new JsonObject
            {
                ["x0"] = MathF.Round(x0), ["z0"] = MathF.Round(z0), ["x1"] = MathF.Round(x1), ["z1"] = MathF.Round(z1),
            });
        }
    }

    public bool Key(string key, ToolModifiers modifiers)
    {
        if (key == "Escape" && Box is not null)
        {
            Box = null;
            _captureFrom = null;
            return true;
        }
        if (modifiers.HasFlag(ToolModifiers.Control) || modifiers.HasFlag(ToolModifiers.Alt) || key is not ("Q" or "E"))
        {
            return false;
        }
        double step = modifiers.HasFlag(ToolModifiers.Shift) ? 90 : 15;
        double rotation = Shape.Rotation + (key == "E" ? step : -step);
        Shape.Rotation = rotation > 180 ? rotation - 360 : rotation <= -180 ? rotation + 360 : rotation;
        _model.Status = $"The stamp turned {Shape.Rotation:0}°";
        return true;
    }

    /// <summary>
    /// Takes a captured stamp: its picture and the settings that put its part of the map back.
    /// </summary>
    public void Take(string file, string key, string title, HeightStampCapture capture)
    {
        Shape.ChooseFile(key, title, file);
        Shape.Rotation = 0;
        Shape.RandomRotation = false;
        Mode = HeightStampMode.Set;
        // a pixel an ogrid apart, as the samples were
        Radius = Math.Clamp(Math.Max(capture.Width, capture.Height) / 2.0, 1, MaximumRadius);
        Height = Math.Round(capture.Highest - capture.Lowest, 3);
        BaseFromGround = false;
        Base = Math.Round(capture.Lowest, 3);
        Edge = 0;
    }
}
