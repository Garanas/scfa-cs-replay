using System.Numerics;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.Rendering;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Controls;

/// <summary>
/// The navigation mesh in the 3D view (user story T-8): the connected regions of the shown layer on
/// the terrain, in walkability's place in the overlays' pass.
/// </summary>
public sealed partial class TerrainViewGpu
{
    /// <summary>
    /// The regions' colours for the overlays' pass, and what names them (made again only for
    /// another mesh, layer, chosen region or theme).
    /// </summary>
    private (object Of, Func<uint[]> Pixels) RegionOverlay(NavigationViewModel navigation, NavGrid grid)
    {
        Vector4[] palette = [.. Enumerable.Range(1, NavigationViewModel.PaletteSize).Select(n => ThemeColorWithAlpha($"MapRegion{n}Brush"))];
        Vector4 blocked = ThemeColorWithAlpha("MapOutsidePlayableBrush");
        Vector4 chosen = ThemeColorWithAlpha("MapRegionChosenBrush");
        int? region = navigation.ChosenRegion;
        return ((grid, region, palette[0], blocked, chosen), () => RegionPixels.Of(grid, palette, blocked, region, chosen));
    }
}
