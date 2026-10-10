using System.Numerics;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.Rendering;
using FAForever.MapEditor.App.ViewModels;

namespace FAForever.MapEditor.App.Controls;

/// <summary>
/// The navigation mesh in the top view (user story T-8): the regions of the shown layer, a colour
/// each, one pixel an ogrid, and the routes between starts as lines.
/// </summary>
public sealed partial class HeightmapView
{
    private WriteableBitmap? _regions;
    private (NavGrid Grid, int? Chosen, Overlay Overlay)? _regionsOf;

    /// <summary>
    /// How many routes the top view drew the last time (automation).
    /// </summary>
    public int RoutesDrawn { get; private set; }

    private Vector4 TokenColor(string key) => Token<IBrush>(key) is ISolidColorBrush brush
        ? new Vector4(brush.Color.R / 255f, brush.Color.G / 255f, brush.Color.B / 255f, brush.Color.A / 255f * (float)brush.Opacity)
        : Vector4.One;

    private unsafe void DrawNavigation(DrawingContext context, Overlay overlay, NavigationViewModel navigation)
    {
        RoutesDrawn = 0;
        if (navigation.Grid is not NavGrid grid)
        {
            return;
        }
        if (_regionsOf is not { } made || !ReferenceEquals(made.Grid, grid) || made.Chosen != navigation.ChosenRegion || !ReferenceEquals(made.Overlay, overlay))
        {
            using Performance.Span measure = Performance.Measure("top.regions");
            Vector4[] palette = [.. Enumerable.Range(1, NavigationViewModel.PaletteSize).Select(n => TokenColor($"MapRegion{n}Brush"))];
            uint[] pixels = RegionPixels.Of(grid, palette, TokenColor("MapOutsidePlayableBrush"), navigation.ChosenRegion, TokenColor("MapRegionChosenBrush"));
            _regions?.Dispose();
            _regions = new WriteableBitmap(new PixelSize(grid.Width, grid.Height), new Avalonia.Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            using (ILockedFramebuffer frame = _regions.Lock())
            {
                for (int z = 0; z < grid.Height; z++)
                {
                    fixed (uint* source = &pixels[z * grid.Width])
                    {
                        Buffer.MemoryCopy(source, (void*)(frame.Address + z * frame.RowBytes), frame.RowBytes, grid.Width * sizeof(uint));
                    }
                }
            }
            _regionsOf = (grid, navigation.ChosenRegion, overlay);
        }
        Rect destination = new Rect(_offset.X, _offset.Y, grid.Width * _zoom, grid.Height * _zoom);
        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = _zoom >= 2 ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality }))
        {
            context.DrawImage(_regions!, new Rect(0, 0, grid.Width, grid.Height), destination);
        }

        IPen route = new Pen(Token<IBrush>("MapRouteBrush"), 2);
        foreach (NavRoute line in navigation.RouteLines)
        {
            if (line.Points.Count < 2)
            {
                continue;
            }
            StreamGeometry geometry = new StreamGeometry();
            using (StreamGeometryContext path = geometry.Open())
            {
                path.BeginFigure(MapToControl(line.Points[0].X, line.Points[0].Y), false);
                for (int i = 1; i < line.Points.Count; i++)
                {
                    path.LineTo(MapToControl(line.Points[i].X, line.Points[i].Y));
                }
                path.EndFigure(false);
            }
            context.DrawGeometry(null, overlay.Shadow, geometry);
            context.DrawGeometry(null, route, geometry);
            RoutesDrawn++;
        }
    }
}
