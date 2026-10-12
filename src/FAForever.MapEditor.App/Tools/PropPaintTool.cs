using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.Tools;

/// <summary>
/// A layer whose mask says where the paint tool may paint props (user story P-3): a stratum of
/// the map, later a mask layer of user story K-1 (which adds its masks here as more choices).
/// </summary>
/// <param name="Name">What the choice says, e.g. <c>Layer 3: SandLight</c>.</param>
/// <param name="Mask">The mask of a version of the map; null when it has none (a choice of none, or a
/// layer the map lost).</param>
public sealed record PaintLayer(string Name, Func<MapDocument, TiledBuffer<byte>?> Mask)
{
    /// <summary>
    /// The choice of no layer: anywhere.
    /// </summary>
    public static PaintLayer None { get; } = new PaintLayer("Anywhere", _ => null);

    public bool IsNone => ReferenceEquals(this, None);

    /// <summary>
    /// What a layer is, apart from the name it is shown by: a stratum's name follows its texture, so
    /// the choice of a stratum survives a change of texture.
    /// </summary>
    public string Key { get; init; } = Name;

    /// <summary>
    /// A stratum's mask (layer 1 to 8).
    /// </summary>
    public static PaintLayer Stratum(Stratum stratum) => new PaintLayer(stratum.Name, document =>
        stratum.Layer >= 1 && stratum.Layer <= document.Terrain.StratumMasks.Layers.Length ? document.Terrain.StratumMasks.Layers[stratum.Layer - 1] : null)
    {
        Key = $"stratum:{stratum.Layer}",
    };

    /// <summary>
    /// A mask for tools of the map (user story K-1), by its name.
    /// </summary>
    public static PaintLayer OfMask(string name) => new PaintLayer($"Mask: {name}", document => document.Masks.GetValueOrDefault(name));

    public override string ToString() => Name;
}

/// <summary>
/// Paints props of the palette with a round brush (user stories P-2, P-3): each dab puts props of
/// the palette, chosen by weight, at places at random in its circle, as many as the density asks,
/// none closer to another prop than the spacing, standing on the terrain as the Props panel's
/// Placing says. It may paint only where a stratum or a mask for tools covers and not where
/// another does. A stroke is one step of undo.
/// </summary>
public sealed partial class PropPaintTool : ObservableObject, IMapTool
{
    private readonly MainViewModel _model;
    private BrushStroke? _stroke;
    private PropScatter? _scatter;
    private bool _first;

    public PropPaintTool(MainViewModel model)
    {
        _model = model;
        model.MapChanged += (_, _) =>
        {
            // the layers of the previous map are not the new map's, whatever they are called
            if (!ReferenceEquals(model.Map, _layersOf))
            {
                _layersOf = model.Map;
                OnlyOn = PaintLayer.None;
                NotOn = PaintLayer.None;
            }
            if (!_watchingStrata)
            {
                // a stratum's texture changed (user story S-2): its name in the choices follows
                _watchingStrata = true;
                model.StrataTool.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(StrataTool.Strata))
                    {
                        ReadLayers();
                    }
                };
            }
            ReadLayers();
        };
        // a mask added, renamed or removed (user story K-1) is a layer to paint on, or no longer
        model.DocumentChanged += (_, _) =>
        {
            if (!ReferenceEquals(_model.ShownDocument?.Masks, _masks))
            {
                ReadLayers();
            }
        };
    }

    // the masks for tools the layers were read from
    private object? _masks;
    private FAForever.MapEditor.Project.EditedMap? _layersOf;
    private bool _watchingStrata;

    /// <summary>
    /// The radius in ogrids.
    /// </summary>
    [ObservableProperty]
    public partial double Radius { get; set; } = 12;

    /// <summary>
    /// Props per 100 square ogrids (a square of 10 by 10) a stroke paints, up to the spacing.
    /// </summary>
    [ObservableProperty]
    public partial double Density { get; set; } = 3;

    /// <summary>
    /// The least distance between props, in ogrids.
    /// </summary>
    [ObservableProperty]
    public partial double Spacing { get; set; } = 2;

    /// <summary>
    /// The layers to choose from: anywhere, then the map's strata.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<PaintLayer> Layers { get; private set; } = [PaintLayer.None];

    /// <summary>
    /// Paints only where this layer covers at least <see cref="Threshold"/>; <see cref="PaintLayer.None"/>: anywhere.
    /// </summary>
    [ObservableProperty]
    public partial PaintLayer OnlyOn { get; set; } = PaintLayer.None;

    /// <summary>
    /// Paints nowhere this layer covers at least <see cref="Threshold"/>; <see cref="PaintLayer.None"/>: no such place.
    /// </summary>
    [ObservableProperty]
    public partial PaintLayer NotOn { get; set; } = PaintLayer.None;

    /// <summary>
    /// How much a layer covers to count, from 0 to 1.
    /// </summary>
    [ObservableProperty]
    public partial double Threshold { get; set; } = 0.5;

    public double CursorRadius => Radius;

    /// <summary>
    /// Chooses the layers by name (automation, presets); a name the map has not is an error.
    /// </summary>
    /// <exception cref="ArgumentException">No layer has the name.</exception>
    public PaintLayer LayerNamed(string? name) =>
        string.IsNullOrEmpty(name) || name.Equals(PaintLayer.None.Name, StringComparison.OrdinalIgnoreCase)
            ? PaintLayer.None
            : Layers.FirstOrDefault(layer => layer.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
              ?? Layers.FirstOrDefault(layer => layer.Name.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase))
              ?? Layers.FirstOrDefault(layer => layer.Name.Equals(TitleOfKey(name), StringComparison.OrdinalIgnoreCase))
              ?? Layers.FirstOrDefault(layer => layer.Name.StartsWith(TitleOfKey(name) + ":", StringComparison.OrdinalIgnoreCase))
              ?? throw new ArgumentException($"There is no layer '{name}': {string.Join(", ", Layers.Select(layer => layer.Name))}");

    /// <summary>
    /// The list's name of a mask named the one way layers name it (<c>mask:forest</c>,
    /// <c>stratum:3</c>, <c>stratum 3</c>): <c>Mask: forest</c>, <c>Layer 3</c>.
    /// </summary>
    private static string TitleOfKey(string name)
    {
        string parsed = RuleLayers.Parse(name);
        return MaskRuleSources.IsStratum(parsed, out int layer) ? $"Layer {layer}" : RuleLayers.MaskPrefix + parsed;
    }

    /// <summary>
    /// The conditions of where it paints, for a version of the map.
    /// </summary>
    public IReadOnlyList<IPlacementCondition> ConditionsFor(MapDocument document)
    {
        List<IPlacementCondition> conditions = [];
        int width = document.Terrain.Heightmap.Width;
        int height = document.Terrain.Heightmap.Height;
        if (OnlyOn.Mask(document) is TiledBuffer<byte> only)
        {
            conditions.Add(new MaskCondition(only, width, height, (float)Threshold));
        }
        if (NotOn.Mask(document) is TiledBuffer<byte> not)
        {
            conditions.Add(new MaskCondition(not, width, height, (float)Threshold, Inverted: true));
        }
        return conditions;
    }

    public void Press(float x, float z)
    {
        MapDocument? document = _model.ShownDocument;
        if (document is null)
        {
            return;
        }
        IReadOnlyList<PaletteEntry> palette = _model.Props.PaletteEntries;
        if (!palette.Any(entry => entry.Weight > 0))
        {
            _model.Status = "The palette is empty: add props to it in the Props panel, or choose props on the shelf while painting";
            return;
        }
        using Performance.Span measure = Performance.Measure("tool.props-paint-start");
        _scatter = new PropScatter(document.Props, palette, _model.Props.Placement, _model.Props.Random)
        {
            Density = (float)Density,
            Spacing = (float)Spacing,
            Conditions = ConditionsFor(document),
            // the props scattered are mirrored, not the dabs: each part gets the same props (F-12)
            Images = _model.EditTransforms(document),
        };
        _first = true;
        float radius = (float)Radius;
        // the first dab covers its circle; each next one the strip it moves on, a quarter of the radius
        float strip = 2 * radius * Math.Max(0.5f, radius / 4);
        _stroke = new BrushStroke(_model, "Paint props", Radius, x, z, (edit, dab) =>
        {
            (float at, float to) = (dab.X, dab.Z);
            PropScatter scatter = _scatter!;
            int tries = scatter.Tries(_first ? MathF.PI * radius * radius : strip);
            _first = false;
            List<Prop> props = scatter.Dab(edit.Document.Terrain.Heightmap, at, to, radius, tries);
            if (props.Count > 0)
            {
                edit.Apply(current => current.AddProps(props, out _));
            }
        }, mirror: false);
    }

    public void Drag(float x, float z) => _stroke?.MoveTo(x, z);

    public void Release()
    {
        _stroke?.End();
        _stroke = null;
        _scatter = null;
    }

    /// <summary>
    /// The open map's strata and masks for tools as choices; a chosen one the map has not becomes
    /// anywhere.
    /// </summary>
    private void ReadLayers()
    {
        IReadOnlyList<Stratum> strata = _model.StrataTool.Strata;
        MapDocument? document = _model.ShownDocument;
        _masks = document?.Masks;
        Layers = [PaintLayer.None, .. strata.Select(PaintLayer.Stratum), .. (document?.Masks.Keys ?? []).Select(PaintLayer.OfMask)];
        OnlyOn = Layers.FirstOrDefault(layer => layer.Key == OnlyOn.Key) ?? PaintLayer.None;
        NotOn = Layers.FirstOrDefault(layer => layer.Key == NotOn.Key) ?? PaintLayer.None;
        _model.Logger("Editor").LogDebug("The paint tool offers {Count} layers to paint on", Layers.Count - 1);
    }
}
