using System.Numerics;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.Rendering;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Controls;

/// <summary>
/// The navigation mesh in the 3D view (user story T-8): the regions of the shown layer on the
/// terrain, in walkability's place in the overlays' pass, and the routes between starts as lines on
/// the ground.
/// </summary>
public sealed partial class TerrainViewGpu
{
    /// <summary>
    /// How many routes the 3D view drew the last frame (automation).
    /// </summary>
    public int RoutesDrawn { get; private set; }

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

    /// <summary>
    /// The routes of the shown layer as lines on the terrain, through at most a point every few
    /// ogrids.
    /// </summary>
    private void Routes(MapDocument document, ToolView view, List<ToolShape> shapes)
    {
        using Performance.Span measure = Performance.Measure("3d.routes");
        RoutesDrawn = 0;
        Heightmap heights = document.Terrain.Heightmap;
        foreach (NavRoute route in _model!.Navigation.RouteLines)
        {
            int step = Math.Max(1, route.Points.Count / 256);
            List<(float X, float Z)> points = [];
            for (int i = 0; i < route.Points.Count; i += step)
            {
                points.Add((route.Points[i].X, route.Points[i].Y));
            }
            points.Add((route.Points[^1].X, route.Points[^1].Y));
            if (points.Count >= 2 && TerrainLine(heights, view, points) is Vector2[] line)
            {
                shapes.Add(new ToolLine(line, false, 4, ToolInk.Shadow));
                shapes.Add(new ToolLine(line, false, 2, ToolInk.Hover));
                RoutesDrawn++;
            }
        }
    }
}
