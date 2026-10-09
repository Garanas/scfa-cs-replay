using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;

namespace FAForever.MapEditor.App.Tools;

/// <summary>
/// Raises, lowers, smooths or flattens the heights with a round brush (user story T-1).
/// </summary>
public sealed partial class SculptTool(MainViewModel model) : ObservableObject, IMapTool
{
    private BrushStroke? _stroke;

    public IReadOnlyList<HeightBrushMode> Modes { get; } = Enum.GetValues<HeightBrushMode>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFlattening))]
    public partial HeightBrushMode Mode { get; set; } = HeightBrushMode.Raise;

    /// <summary>
    /// Whether it flattens: only then does the height to flatten to matter.
    /// </summary>
    public bool IsFlattening => Mode == HeightBrushMode.Flatten;

    /// <summary>
    /// The radius in ogrids.
    /// </summary>
    [ObservableProperty]
    public partial double Radius { get; set; } = 12;

    /// <summary>
    /// Game units at the centre for raising and lowering, 0 to 1 for smoothing and flattening.
    /// </summary>
    [ObservableProperty]
    public partial double Strength { get; set; } = 0.5;

    /// <summary>
    /// The height in game units that flattening moves towards.
    /// </summary>
    [ObservableProperty]
    public partial double Target { get; set; } = 20;

    public double CursorRadius => Radius;

    public void Press(float x, float z)
    {
        HeightBrush brush = new HeightBrush
        {
            Mode = Mode,
            Radius = (float)Radius,
            Strength = (float)Strength,
            Target = (float)Target,
        };
        float scale = (model.ShownDocument ?? throw new InvalidOperationException("No map is open")).Terrain.Heightmap.Scale;
        string description = Mode switch
        {
            HeightBrushMode.Raise => "Raise terrain",
            HeightBrushMode.Lower => "Lower terrain",
            HeightBrushMode.Smooth => "Smooth terrain",
            _ => "Flatten terrain",
        };
        _stroke = new BrushStroke(model, description, Radius, x, z, (edit, at, to) => brush.Apply(edit.Heightmap, scale, at, to));
    }

    public void Drag(float x, float z) => _stroke?.MoveTo(x, z);

    public void Release()
    {
        _stroke?.End();
        _stroke = null;
    }
}
