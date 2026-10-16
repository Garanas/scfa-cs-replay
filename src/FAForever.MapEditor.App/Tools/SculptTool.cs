using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tools;

/// <summary>
/// Changes the heights with a brush, round or of a picture (user story T-1): raises, lowers,
/// smooths, flattens, sets them to a height, adds noise, erodes slopes, or scales them around a
/// base; a round brush with a choice of falloff, and the heights it makes kept within limits.
/// </summary>
public sealed partial class SculptTool : ObservableObject, IShapedBrushTool
{
    private readonly MainViewModel _model;
    private BrushStroke? _stroke;

    public SculptTool(MainViewModel model)
    {
        _model = model;
        Shape = new BrushShapeSettings(model);
        Shape.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BrushShapeSettings.IsShaped))
            {
                OnPropertyChanged(nameof(StrengthMaximum));
            }
        };
    }

    public BrushShapeSettings Shape { get; }

    public IReadOnlyList<HeightBrushMode> Modes { get; } = Enum.GetValues<HeightBrushMode>();

    /// <summary>
    /// The falloffs a round brush can have.
    /// </summary>
    public IReadOnlyList<BrushFalloff> Falloffs { get; } = Enum.GetValues<BrushFalloff>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFlattening), nameof(UsesTarget), nameof(TargetLabel), nameof(UsesStrength), nameof(IsNoise), nameof(IsScaling), nameof(StrengthMaximum))]
    public partial HeightBrushMode Mode { get; set; } = HeightBrushMode.Raise;

    /// <summary>
    /// Whether it flattens.
    /// </summary>
    public bool IsFlattening => Mode == HeightBrushMode.Flatten;

    /// <summary>
    /// Whether the height <see cref="Target"/> matters: flattening, setting, and the base of scaling.
    /// </summary>
    public bool UsesTarget => Mode is HeightBrushMode.Flatten or HeightBrushMode.SetHeight or HeightBrushMode.Scale;

    /// <summary>
    /// What <see cref="Target"/> is to the mode, for its label.
    /// </summary>
    public string TargetLabel => Mode switch
    {
        HeightBrushMode.SetHeight => "Height",
        HeightBrushMode.Scale => "Around",
        _ => "Flatten to",
    };

    /// <summary>
    /// Whether the strength matters: setting a height goes all the way, scaling goes by its factor.
    /// </summary>
    public bool UsesStrength => Mode is not (HeightBrushMode.SetHeight or HeightBrushMode.Scale);

    public bool IsNoise => Mode == HeightBrushMode.Noise;

    public bool IsScaling => Mode == HeightBrushMode.Scale;

    /// <summary>
    /// How a round brush fades from its centre to its radius.
    /// </summary>
    [ObservableProperty]
    public partial BrushFalloff Falloff { get; set; } = BrushFalloff.Smooth;

    /// <summary>
    /// The radius in ogrids.
    /// </summary>
    [ObservableProperty]
    public partial double Radius { get; set; } = 12;

    /// <summary>
    /// Game units at the centre for raising, lowering and noise, 0 to 1 for smoothing, flattening
    /// and eroding.
    /// </summary>
    [ObservableProperty]
    public partial double Strength { get; set; } = 0.5;

    /// <summary>
    /// The height in game units that flattening moves towards, setting sets, and scaling scales around.
    /// </summary>
    [ObservableProperty]
    public partial double Target { get; set; } = 20;

    /// <summary>
    /// What scaling multiplies the heights' distance from <see cref="Target"/> by at the centre.
    /// </summary>
    [ObservableProperty]
    public partial double Factor { get; set; } = 1.1;

    /// <summary>
    /// The size of the noise's features, in ogrids.
    /// </summary>
    [ObservableProperty]
    public partial double NoiseScale { get; set; } = 16;

    /// <summary>
    /// Whether the heights it makes are kept between <see cref="MinHeight"/> and <see cref="MaxHeight"/>.
    /// </summary>
    [ObservableProperty]
    public partial bool Limited { get; set; }

    /// <summary>
    /// The lowest height it makes while <see cref="Limited"/>, in game units.
    /// </summary>
    [ObservableProperty]
    public partial double MinHeight { get; set; }

    /// <summary>
    /// The highest height it makes while <see cref="Limited"/>, in game units.
    /// </summary>
    [ObservableProperty]
    public partial double MaxHeight { get; set; } = 256;

    public double CursorRadius => Radius;

    /// <summary>
    /// The most the strength slider goes to: a picture raises or lowers up to 50 game units in one
    /// dab, a mountain at a click; round, 5.
    /// </summary>
    public double StrengthMaximum => Shape.IsShaped && Mode is HeightBrushMode.Raise or HeightBrushMode.Lower or HeightBrushMode.Noise ? 50 : 5;

    public void Press(float x, float z)
    {
        if (BrushShapeSettings.TryRead(_model, Shape) is not (true, var shape))
        {
            return;
        }
        HeightBrush brush = new HeightBrush
        {
            Mode = Mode,
            Radius = (float)Radius,
            Strength = (float)Strength,
            Target = (float)Target,
            Factor = (float)Factor,
            NoiseScale = (float)Math.Max(1, NoiseScale),
            // a new noise with every stroke, the same within it
            Seed = Random.Shared.Next(),
            Shape = shape,
            Falloff = Falloff,
            MinHeight = Limited ? (float)MinHeight : null,
            MaxHeight = Limited ? (float)MaxHeight : null,
        };
        MapDocument document = _model.ShownDocument ?? throw new InvalidOperationException("No map is open");
        // with height layers (T-5) the stroke goes into the chosen one, which must take it
        if (HeightLayers.SculptProblem(document, _model.HeightStack.Chosen, _model.ShownHeights) is string problem)
        {
            _model.Status = problem;
            return;
        }
        float scale = document.Terrain.Heightmap.Scale;
        string description = Mode switch
        {
            HeightBrushMode.Raise => "Raise terrain",
            HeightBrushMode.Lower => "Lower terrain",
            HeightBrushMode.Smooth => "Smooth terrain",
            HeightBrushMode.SetHeight => "Set terrain height",
            HeightBrushMode.Noise => "Add noise to terrain",
            HeightBrushMode.Erode => "Erode terrain",
            HeightBrushMode.Scale => "Scale terrain",
            _ => "Flatten terrain",
        };
        _stroke = new BrushStroke(_model, description, Radius, x, z,
            (edit, dab) => (brush with { Rotation = dab.Rotation, Mirrored = dab.Mirrored, NoisePlace = dab.Image.Inverse() }).Apply(edit.Heightmap, scale, dab.X, dab.Z),
            shape is null ? null : Shape.NextRotation);
    }

    public void Drag(float x, float z) => _stroke?.MoveTo(x, z);

    public void Release()
    {
        _stroke?.End();
        _stroke = null;
    }
}
