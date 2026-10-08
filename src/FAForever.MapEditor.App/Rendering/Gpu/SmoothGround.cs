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

    public static SmoothGround From(Heightmap heightmap)
    {
        ushort[] samples = heightmap.Samples.ToArray();
        float[] heights = new float[samples.Length];
        for (int i = 0; i < samples.Length; i++)
        {
            heights[i] = samples[i] * heightmap.Scale;
        }
        return FromHeights(heightmap.Width, heightmap.Height, heights);
    }

    /// <summary>
    /// From heights in game units, one per ogrid corner, the first row first.
    /// </summary>
    public static SmoothGround FromHeights(int width, int height, float[] heights)
    {
        SmoothGround ground = new SmoothGround();
        (int w, int h, float[] level) = Average(width, height, heights, FirstCell);
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
