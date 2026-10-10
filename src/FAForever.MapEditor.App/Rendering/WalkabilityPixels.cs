using System.Numerics;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Generation;

namespace FAForever.MapEditor.App.Rendering;

/// <summary>
/// Walkability (user story T-7) as a colour an ogrid, for the 3D view: where units cannot go in
/// the blocked colour, and while the changes show the ogrids blocked or opened since the time
/// before in theirs; nothing where units go.
/// </summary>
public static class WalkabilityPixels
{
    /// <returns>Premultiplied <c>0xAARRGGBB</c>, a row per z.</returns>
    public static uint[] Of(NavGrid grid, WalkabilityChanges? changes, Vector4 blocked, Vector4 newlyBlocked, Vector4 opened)
    {
        static uint Premultiplied(Vector4 color) =>
            (uint)(color.W * 255) << 24 | (uint)(color.X * color.W * 255) << 16 | (uint)(color.Y * color.W * 255) << 8 | (uint)(color.Z * color.W * 255);
        uint blockedPixel = Premultiplied(blocked);
        uint newlyPixel = Premultiplied(newlyBlocked);
        uint openedPixel = Premultiplied(opened);
        uint[] pixels = new uint[grid.Cells.Length];
        WalkabilityChange[]? changed = changes?.Cells;
        Parallel.For(0, grid.Height, z =>
        {
            for (int x = 0; x < grid.Width; x++)
            {
                int i = z * grid.Width + x;
                pixels[i] = changed?[i] switch
                {
                    WalkabilityChange.Opened => openedPixel,
                    WalkabilityChange.Blocked => newlyPixel,
                    _ => grid.Cells[i] > 0 ? 0u : blockedPixel,
                };
            }
        });
        return pixels;
    }
}
