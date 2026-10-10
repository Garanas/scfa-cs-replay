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
/// S-1), where the slope and height are within limits when those are on. The layer it paints is
/// chosen in the Paint mode's panel; the top view shows its mask while the tool is active.
/// </summary>
public sealed partial class StrataTool : GridPaintTool
{
    public StrataTool(MainViewModel model) : base(model)
    {
        model.MapChanged += (_, _) => ReadStrata();
    }

    public override string PresetName => "strata";

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
    /// Where it paints, by slope and height.
    /// </summary>
    public PaintLimitSettings Limiting { get; } = new PaintLimitSettings();

    public override PaintTarget? Target => Painted is Stratum stratum ? PaintTarget.Stratum(stratum.Layer) : null;

    protected override PaintLimits? Limits => Limiting.ToLimits();

    /// <summary>
    /// Paints the mask of layer <paramref name="layer"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The map has no such layer with a mask.</exception>
    public void Choose(int layer) =>
        Painted = Strata.FirstOrDefault(stratum => stratum.Layer == layer)
            ?? throw new ArgumentOutOfRangeException(nameof(layer), $"The map has no layer {layer} with a mask");

    partial void OnPaintedChanged(Stratum? value) => OnTargetChanged();

    private void ReadStrata()
    {
        Terrain? terrain = Model.Map?.Document.Terrain;
        Strata = terrain is null
            ? []
            : [.. Enumerable.Range(1, terrain.StratumMasks.Layers.Length).Select(layer => new Stratum(layer,
                layer < terrain.Layers.Length && terrain.Layers[layer].AlbedoPath.Length > 0
                    ? $"Layer {layer}: {Path.GetFileNameWithoutExtension(terrain.Layers[layer].AlbedoPath)}"
                    : $"Layer {layer} (no texture)"))];
        Painted = Strata.FirstOrDefault();
    }
}
