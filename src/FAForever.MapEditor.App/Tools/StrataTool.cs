using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tools;

/// <summary>
/// A terrain layer with a mask, which the strata tool paints.
/// </summary>
/// <param name="Layer">1 to 8 (1 to 4 in version 53).</param>
/// <param name="Name">The layer and the name of its texture, e.g. <c>Layer 3: SandLight</c>.</param>
public sealed record Stratum(int Layer, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// Paints or erases the mask of a terrain layer with a brush, round or of a picture (user story
/// S-1). The layer it paints is chosen in the Paint mode's panel; the top view shows its mask while
/// the tool is active.
/// </summary>
public sealed partial class StrataTool : ObservableObject, IShapedBrushTool
{
    private readonly MainViewModel _model;
    private BrushStroke? _stroke;

    public StrataTool(MainViewModel model)
    {
        _model = model;
        Shape = new BrushShapeSettings(model);
        model.MapChanged += (_, _) => ReadStrata();
    }

    /// <summary>
    /// The open map's layers that have a mask, by the name of their texture.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<Stratum> Strata { get; private set; } = [];

    /// <summary>
    /// The layer it paints; null while no map is open.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Layer))]
    public partial Stratum? Painted { get; set; }

    /// <summary>
    /// The layer it paints, 1 to 8; 0 while no map is open.
    /// </summary>
    public int Layer => Painted?.Layer ?? 0;

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
    /// Whether it takes the layer away instead of painting it.
    /// </summary>
    [ObservableProperty]
    public partial bool Erase { get; set; }

    public double CursorRadius => Radius;

    public BrushShapeSettings Shape { get; }

    /// <summary>
    /// Paints the mask of layer <paramref name="layer"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The map has no such layer with a mask.</exception>
    public void Choose(int layer) =>
        Painted = Strata.FirstOrDefault(stratum => stratum.Layer == layer)
            ?? throw new ArgumentOutOfRangeException(nameof(layer), $"The map has no layer {layer} with a mask");

    partial void OnPaintedChanged(Stratum? value) => _model.Redraw();

    public void Press(float x, float z)
    {
        if (Painted is not Stratum stratum || _model.ShownDocument is not MapDocument document
            || BrushShapeSettings.TryRead(_model, Shape) is not (true, var shape))
        {
            return;
        }
        MaskBrush brush = new MaskBrush
        {
            Radius = (float)Radius,
            Strength = (float)Strength,
            Erase = Erase,
            Shape = shape,
        };
        (int width, int height) = (document.Width, document.Height);
        _stroke = new BrushStroke(_model, $"{(Erase ? "Erase" : "Paint")} layer {stratum.Layer}", Radius, x, z,
            (edit, at, to) => (brush with { Rotation = Shape.NextRotation() }).Apply(edit.StratumMask(stratum.Layer), width, height, at, to));
    }

    public void Drag(float x, float z) => _stroke?.MoveTo(x, z);

    public void Release()
    {
        _stroke?.End();
        _stroke = null;
    }

    private void ReadStrata()
    {
        Terrain? terrain = _model.Map?.Document.Terrain;
        Strata = terrain is null
            ? []
            : [.. Enumerable.Range(1, terrain.StratumMasks.Layers.Length).Select(layer => new Stratum(layer,
                layer < terrain.Layers.Length && terrain.Layers[layer].AlbedoPath.Length > 0
                    ? $"Layer {layer}: {Path.GetFileNameWithoutExtension(terrain.Layers[layer].AlbedoPath)}"
                    : $"Layer {layer} (no texture)"))];
        Painted = Strata.FirstOrDefault();
    }
}
