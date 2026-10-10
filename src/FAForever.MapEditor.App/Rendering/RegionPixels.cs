using System.Numerics;
using FAForever.FileFormats.Map;

namespace FAForever.MapEditor.App.Rendering;

/// <summary>
/// The navigation mesh's regions of one layer (user story T-8) as a colour an ogrid, for both views:
/// each region in a colour of the palette in turn (by its number), the chosen region in its own
/// colour, and where units of the layer cannot go darkened.
/// </summary>
public static class RegionPixels
{
    /// <param name="palette">The regions' colours (<c>MapRegion1Brush</c> ...), straight alpha.</param>
    /// <param name="chosen">The number of the chosen region, or null.</param>
    /// <returns>Premultiplied <c>0xAARRGGBB</c>, a row per z.</returns>
    public static uint[] Of(NavGrid grid, IReadOnlyList<Vector4> palette, Vector4 blocked, int? chosen, Vector4 chosenColor)
    {
        uint[] colours = [.. palette.Select(Premultiplied)];
        uint blockedPixel = Premultiplied(blocked);
        uint chosenPixel = Premultiplied(chosenColor);
        uint[] pixels = new uint[grid.Cells.Length];
        int[] cells = grid.Cells;
        int width = grid.Width;
        Parallel.For(0, grid.Height, z =>
        {
            for (int x = 0; x < width; x++)
            {
                int i = z * width + x;
                int id = cells[i];
                pixels[i] = id <= 0 ? blockedPixel : id == chosen ? chosenPixel : colours[(id - 1) % colours.Length];
            }
        });
        return pixels;
    }

    private static uint Premultiplied(Vector4 color) =>
        (uint)(color.W * 255) << 24 | (uint)(color.X * color.W * 255) << 16 | (uint)(color.Y * color.W * 255) << 8 | (uint)(color.Z * color.W * 255);
}
