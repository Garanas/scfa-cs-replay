using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Rendering.Gpu;

/// <summary>
/// The terrain blurred, for the camera to rest on: averages over squares of 8, 16, 32, ... ogrids,
/// read between their centres and between two sizes, so the surface is smooth at every size. The
/// further the camera, the larger the squares it reads (<see cref="Height"/>): a spike or a cliff
/// does not throw it up and down.
/// </summary>
public sealed class SmoothGround
{
    private const int FirstCell = 8;

    // level n: the average height of each square of FirstCell × 2^n ogrids
    private readonly List<(int Width, int Height, float[] Heights)> _levels = [];

    private SmoothGround()
    {
    }

    /// <summary>
    /// A flat ground at a height, until there is a map.
    /// </summary>
    public static SmoothGround Flat(float height) => FromHeights(1, 1, [height]);

    /// <summary>
    /// From the heights of a map, one per ogrid corner.
    /// </summary>
    public static SmoothGround From(Heightmap heightmap)
    {
        // the first level straight from the samples, its rows spread over the processors
        int w = CellsOf(heightmap.Samples.Width, FirstCell);
        int h = CellsOf(heightmap.Samples.Height, FirstCell);
        float[] level = new float[w * h];
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                level[y * w + x] = AverageOf(heightmap, x, y);
            }
        });
        return FromFirstLevel(w, h, level);
    }

    /// <summary>
    /// Follows the heights from <paramref name="before"/> (what it was made from) to
    /// <paramref name="after"/>: only the squares over the tiles that changed are averaged again,
    /// and the larger squares holding them, so a brush's dab on an 81 km map costs a fraction of a
    /// millisecond. Another size or scale, or most of the map changed, makes it anew.
    /// </summary>
    /// <returns>The ground of <paramref name="after"/>: this one, changed, or a new one.</returns>
    public SmoothGround Update(Heightmap before, Heightmap after)
    {
        TiledBuffer<ushort> samples = after.Samples;
        if (MustRemake(before, after) || _levels.Count == 0 || _levels[0].Width != CellsOf(samples.Width, FirstCell))
        {
            return From(after);
        }
        List<(int X, int Y)> tiles = [.. samples.ChangedTiles(before.Samples)];
        if (tiles.Count * 2 > samples.TilesX * samples.TilesY)
        {
            return From(after);
        }
        const int cellsPerTile = TiledBuffer<ushort>.TileSize / FirstCell;
        foreach ((int tileX, int tileY) in tiles)
        {
            // the squares of the first level over the tile, then each larger square that holds them
            int x0 = tileX * cellsPerTile, y0 = tileY * cellsPerTile, x1 = x0 + cellsPerTile, y1 = y0 + cellsPerTile;
            for (int level = 0; level < _levels.Count; level++)
            {
                (int width, int height, float[] heights) = _levels[level];
                x1 = Math.Min(x1, width);
                y1 = Math.Min(y1, height);
                for (int y = y0; y < y1; y++)
                {
                    for (int x = x0; x < x1; x++)
                    {
                        heights[y * width + x] = level == 0 ? AverageOf(after, x, y) : AverageOf(_levels[level - 1], x, y);
                    }
                }
                x0 /= 2;
                y0 /= 2;
                x1 = (x1 + 1) / 2;
                y1 = (y1 + 1) / 2;
            }
        }
        return this;
    }

    /// <summary>
    /// Whether the ground of <paramref name="before"/> cannot follow to <paramref name="after"/>
    /// and is made anew: another size or scale of heights.
    /// </summary>
    public static bool MustRemake(Heightmap before, Heightmap after) =>
        before.Samples.Width != after.Samples.Width || before.Samples.Height != after.Samples.Height || before.Scale != after.Scale;

    private static int CellsOf(int size, int factor) => Math.Max(1, (size + factor - 1) / factor);

    /// <summary>
    /// The average height of the samples in a square of the first level.
    /// </summary>
    private static float AverageOf(Heightmap heightmap, int cellX, int cellY)
    {
        TiledBuffer<ushort> samples = heightmap.Samples;
        int xEnd = Math.Min((cellX + 1) * FirstCell, samples.Width);
        int yEnd = Math.Min((cellY + 1) * FirstCell, samples.Height);
        float sum = 0;
        int count = 0;
        for (int y = cellY * FirstCell; y < yEnd; y++)
        {
            for (int x = cellX * FirstCell; x < xEnd; x++)
            {
                sum += samples[x, y] * heightmap.Scale;
                count++;
            }
        }
        return sum / count;
    }

    /// <summary>
    /// The average of the (up to) four squares of the level below that a square holds.
    /// </summary>
    private static float AverageOf((int Width, int Height, float[] Heights) below, int cellX, int cellY)
    {
        float sum = 0;
        int count = 0;
        for (int y = cellY * 2; y < Math.Min(cellY * 2 + 2, below.Height); y++)
        {
            for (int x = cellX * 2; x < Math.Min(cellX * 2 + 2, below.Width); x++)
            {
                sum += below.Heights[y * below.Width + x];
                count++;
            }
        }
        return sum / count;
    }

    /// <summary>
    /// From heights in game units, one per ogrid corner, the first row first.
    /// </summary>
    public static SmoothGround FromHeights(int width, int height, float[] heights)
    {
        (int w, int h, float[] level) = Average(width, height, heights, FirstCell);
        return FromFirstLevel(w, h, level);
    }

    private static SmoothGround FromFirstLevel(int w, int h, float[] level)
    {
        SmoothGround ground = new SmoothGround();
        ground._levels.Add((w, h, level));
        while (w > 1 || h > 1)
        {
            (w, h, level) = Average(w, h, level, 2);
            ground._levels.Add((w, h, level));
        }
        return ground;
    }

    /// <summary>
    /// The height at a place of the map, averaged over about <paramref name="radius"/> ogrids
    /// around it (at least 8).
    /// </summary>
    public float Height(float x, float z, float radius)
    {
        // the level whose squares are as wide as the radius, and how far towards the next
        float position = Math.Clamp(MathF.Log2(Math.Max(radius, FirstCell) / FirstCell), 0, _levels.Count - 1);
        int level = (int)position;
        float fraction = position - level;
        float here = Sample(level, x, z);
        return fraction <= 0 || level + 1 >= _levels.Count ? here : here + (Sample(level + 1, x, z) - here) * fraction;
    }

    private float Sample(int level, float x, float z)
    {
        (int width, int height, float[] heights) = _levels[level];
        float cell = FirstCell * MathF.Pow(2, level);
        // between the centres of the squares, the edge ones reaching out of the map
        float u = Math.Clamp(x / cell - 0.5f, 0, width - 1);
        float v = Math.Clamp(z / cell - 0.5f, 0, height - 1);
        int u0 = (int)u;
        int v0 = (int)v;
        int u1 = Math.Min(u0 + 1, width - 1);
        int v1 = Math.Min(v0 + 1, height - 1);
        float fu = u - u0;
        float fv = v - v0;
        float top = heights[v0 * width + u0] * (1 - fu) + heights[v0 * width + u1] * fu;
        float bottom = heights[v1 * width + u0] * (1 - fu) + heights[v1 * width + u1] * fu;
        return top * (1 - fv) + bottom * fv;
    }

    private static (int Width, int Height, float[] Heights) Average(int width, int height, float[] heights, int factor)
    {
        int w = Math.Max(1, (width + factor - 1) / factor);
        int h = Math.Max(1, (height + factor - 1) / factor);
        float[] result = new float[w * h];
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                float sum = 0;
                int count = 0;
                for (int dy = 0; dy < factor && y * factor + dy < height; dy++)
                {
                    int row = (y * factor + dy) * width;
                    for (int dx = 0; dx < factor && x * factor + dx < width; dx++)
                    {
                        sum += heights[row + x * factor + dx];
                        count++;
                    }
                }
                result[y * w + x] = sum / count;
            }
        });
        return (w, h, result);
    }
}
