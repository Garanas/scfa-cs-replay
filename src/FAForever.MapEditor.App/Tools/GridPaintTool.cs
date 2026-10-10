using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.App.Rendering;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tools;

/// <summary>
/// The core of the tools that paint a grid of bytes of the map with a brush, round or of a
/// picture: a terrain layer's mask (S-1), the water's masks (W-2), the masks for tools (K-1) and
/// the terrain's types (T-6). A tool says what it paints (<see cref="Target"/>) and how the top view
/// shows it (<see cref="Shown"/>); masks blend towards full or empty (<see cref="MaskBrush"/>), a grid
/// of codes is set where the brush works (<see cref="CodeBrush"/>, <see cref="SetsCodes"/>).
/// </summary>
public abstract partial class GridPaintTool : ObservableObject, IShapedBrushTool
{
    private BrushStroke? _stroke;

    protected GridPaintTool(MainViewModel model)
    {
        Model = model;
        Shape = new BrushShapeSettings(model);
    }

    protected MainViewModel Model { get; }

    public BrushShapeSettings Shape { get; }

    /// <summary>
    /// The falloffs a round brush can have.
    /// </summary>
    public IReadOnlyList<BrushFalloff> Falloffs { get; } = Enum.GetValues<BrushFalloff>();

    /// <summary>
    /// The radius in ogrids.
    /// </summary>
    [ObservableProperty]
    public partial double Radius { get; set; } = 12;

    /// <summary>
    /// How far a dab moves the mask at the centre, from 0 to 1.
    /// </summary>
    [ObservableProperty]
    public partial double Strength { get; set; } = 0.3;

    /// <summary>
    /// Whether it takes the mask away instead of painting it.
    /// </summary>
    [ObservableProperty]
    public partial bool Erase { get; set; }

    /// <summary>
    /// How a round brush fades from its centre to its radius.
    /// </summary>
    [ObservableProperty]
    public partial BrushFalloff Falloff { get; set; } = BrushFalloff.Smooth;

    public double CursorRadius => Radius;

    /// <summary>
    /// Its name in brush presets: <c>strata</c>, <c>water</c>, <c>masks</c>, <c>types</c>.
    /// </summary>
    public abstract string PresetName { get; }

    /// <summary>
    /// What it paints now; null when there is nothing to paint (no map, no mask chosen).
    /// </summary>
    public abstract PaintTarget? Target { get; }

    /// <summary>
    /// What the top view lays over the terrain while the tool is active; null for nothing. A tool
    /// raises <see cref="ObservableObject.PropertyChanged"/> for it when it paints something else.
    /// </summary>
    public virtual GridOverlay? Shown => Target is PaintTarget target ? GridOverlay.Mask(target) : null;

    /// <summary>
    /// Whether it sets a code (<see cref="Code"/>) instead of blending a mask.
    /// </summary>
    protected virtual bool SetsCodes => false;

    /// <summary>
    /// The code it sets, when it <see cref="SetsCodes"/>.
    /// </summary>
    protected virtual byte Code => 0;

    /// <summary>
    /// Where it may paint, by the terrain's slope and height; null everywhere.
    /// </summary>
    protected virtual PaintLimits? Limits => null;

    /// <summary>
    /// The step of the history a stroke makes, e.g. <c>Paint layer 3</c>.
    /// </summary>
    protected virtual string Describe(PaintTarget target) => $"{(Erase ? "Erase" : "Paint")} {target.Title}";

    /// <summary>
    /// Says that <see cref="Target"/> and <see cref="Shown"/> changed: another layer or mask.
    /// </summary>
    protected void OnTargetChanged()
    {
        OnPropertyChanged(nameof(Target));
        OnPropertyChanged(nameof(Shown));
    }

    public void Press(float x, float z)
    {
        if (Target is not PaintTarget target || Model.ShownDocument is not MapDocument document
            || BrushShapeSettings.TryRead(Model, Shape) is not (true, var shape))
        {
            return;
        }
        (int width, int height) = (document.Width, document.Height);
        // the heights do not change while a grid is painted: the limits are checked against them as they are
        Heightmap? terrain = Limits is { Limits: true } ? document.Terrain.Heightmap : null;
        Action<EditTransaction, float, float> dab;
        if (SetsCodes)
        {
            CodeBrush brush = new CodeBrush { Radius = (float)Radius, Value = Code, Shape = shape, Falloff = Falloff, Limits = Limits };
            dab = (edit, at, to) => (brush with { Rotation = Shape.NextRotation() }).Apply(edit.Grid(target), width, height, at, to, terrain);
        }
        else
        {
            MaskBrush brush = new MaskBrush { Radius = (float)Radius, Strength = (float)Strength, Erase = Erase, Shape = shape, Falloff = Falloff, Limits = Limits };
            dab = (edit, at, to) => (brush with { Rotation = Shape.NextRotation() }).Apply(edit.Grid(target), width, height, at, to, terrain);
        }
        _stroke = new BrushStroke(Model, Describe(target), Radius, x, z, dab);
    }

    public void Drag(float x, float z) => _stroke?.MoveTo(x, z);

    public void Release()
    {
        _stroke?.End();
        _stroke = null;
    }
}

/// <summary>
/// Limits on where a brush paints, by the terrain's slope and height (user story S-1), as the tool
/// options set them: each pair on or off.
/// </summary>
public sealed partial class PaintLimitSettings : ObservableObject
{
    /// <summary>
    /// Whether it paints only where the slope is between <see cref="MinSlope"/> and <see cref="MaxSlope"/>.
    /// </summary>
    [ObservableProperty]
    public partial bool BySlope { get; set; }

    /// <summary>
    /// In degrees from flat.
    /// </summary>
    [ObservableProperty]
    public partial double MinSlope { get; set; }

    /// <summary>
    /// In degrees from flat.
    /// </summary>
    [ObservableProperty]
    public partial double MaxSlope { get; set; } = 30;

    /// <summary>
    /// Whether it paints only where the height is between <see cref="MinHeight"/> and <see cref="MaxHeight"/>.
    /// </summary>
    [ObservableProperty]
    public partial bool ByHeight { get; set; }

    /// <summary>
    /// In game units.
    /// </summary>
    [ObservableProperty]
    public partial double MinHeight { get; set; }

    /// <summary>
    /// In game units.
    /// </summary>
    [ObservableProperty]
    public partial double MaxHeight { get; set; } = 256;

    /// <summary>
    /// The limits for a brush; null when neither pair is on.
    /// </summary>
    public PaintLimits? ToLimits() => BySlope || ByHeight
        ? new PaintLimits(
            BySlope ? (float)MinSlope : null,
            BySlope ? (float)MaxSlope : null,
            ByHeight ? (float)MinHeight : null,
            ByHeight ? (float)MaxHeight : null)
        : null;
}
