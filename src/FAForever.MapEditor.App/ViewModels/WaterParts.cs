using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// One of the water's three maps (user story W-2), as the Water panel lists it and the water brush
/// paints it.
/// </summary>
/// <param name="PartId">Its entry in the Water panel, e.g. <c>water.foam</c>.</param>
public sealed record WaterMap(WaterMask Mask, string PartId, string Title, string Help)
{
    /// <summary>
    /// Its grid in the map's water.
    /// </summary>
    public TiledBuffer<byte> Of(Water water) => Mask switch
    {
        WaterMask.Foam => water.FoamMask,
        WaterMask.Flatness => water.FlatnessMask,
        _ => water.DepthBiasMask,
    };
}

/// <summary>
/// The water's maps, in the order the Water panel lists them.
/// </summary>
public static class WaterMaps
{
    public static IReadOnlyList<WaterMap> All { get; } =
    [
        new WaterMap(WaterMask.Foam, "water.foam", "Foam", "Where the water foams, at shores and rocks"),
        new WaterMap(WaterMask.Flatness, "water.flatness", "Flatness", "Where the waves die down and the surface lies flat"),
        new WaterMap(WaterMask.DepthBias, "water.depthBias", "Depth bias", "Where the water looks deeper or shallower than it is"),
    ];

    public static WaterMap Of(WaterMask mask) => All.First(map => map.Mask == mask);
}

/// <summary>
/// The inspector's part of a water map: what it is, and the water brush that paints it (its erase,
/// radius and strength; the shape is in the tool's options).
/// </summary>
/// <param name="Brush">The Paint water tool; null until the editor's tools are made.</param>
/// <param name="Stack">The map's stack of layers (wave 9B): its painted layer is what the brush paints.</param>
public sealed record WaterMapPart(WaterMap Map, WaterPaintTool? Brush = null, CoverageStackViewModel? Stack = null)
{
    public bool HasBrush => Brush is not null;

    public bool HasStack => Stack is not null;
}

/// <summary>
/// The inspector's part of the wave generators (user story W-1): their list, add and remove, and the
/// chosen one's fields.
/// </summary>
public sealed record WaveGeneratorsPart(WaterSettingsViewModel Water);
