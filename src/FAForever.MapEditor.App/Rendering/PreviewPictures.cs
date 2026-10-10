using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Rendering;

/// <summary>
/// What an <see cref="Controls.ImagePreview"/> shows: a file, or a grid of the document drawn as a
/// picture. A grid's picture is made once per version of the grid (the grids never change, a new
/// version is a new object) and size, so a preview costs nothing until its grid changes.
/// </summary>
public abstract record PreviewPicture
{
    /// <summary>
    /// The object whose version the picture follows: the grid (pictures are kept as long as it
    /// lives), or a file's path.
    /// </summary>
    public abstract object Key { get; }

    /// <summary>
    /// What distinguishes pictures of the same key: how it is drawn.
    /// </summary>
    public virtual string Variant => GetType().Name;

    /// <summary>
    /// The width and height of the grid, to keep its proportions; null when unknown (a file).
    /// </summary>
    public abstract (int Width, int Height)? GridSize { get; }

    /// <summary>
    /// The picture at most <paramref name="size"/> pixels on its longer side, row by row as
    /// <c>0xAARRGGBB</c>; null when there is nothing to draw (a file that cannot be read).
    /// </summary>
    public abstract (uint[] Pixels, int Width, int Height)? Render(int size);

    /// <summary>
    /// The picture of a layer of a stack: its values with a height ramp (heights) or as grey
    /// (coverage), the mask a mask reference reads, else nothing.
    /// </summary>
    public static PreviewPicture? OfLayer(MapDocument document, LayerStack stack, Layer layer)
    {
        if (layer.Source is MaskRefSource reference)
        {
            return LayerStacks.MaskOf(document, reference.Key) is TiledBuffer<byte> mask ? new CoveragePicture(mask) : null;
        }
        if (stack.Target.IsHeights)
        {
            return layer.Values is TiledBuffer<float> values
                ? new HeightPicture(values, HeightLayerEdits.IsDifference(layer.Combine) && !layer.IsBase, layer.Coverage)
                : null;
        }
        return layer.Coverage is TiledBuffer<byte> coverage ? new CoveragePicture(coverage) : null;
    }

    /// <summary>
    /// The size of a picture of a grid of that size, <paramref name="size"/> on the longer side (a
    /// small grid's pixels are repeated, so its large preview is as large as any other's).
    /// </summary>
    protected static (int Width, int Height) Fit(int width, int height, int size)
    {
        int longer = Math.Max(1, Math.Max(width, height));
        return (Math.Max(1, (int)Math.Round((double)width * size / longer)), Math.Max(1, (int)Math.Round((double)height * size / longer)));
    }
}

/// <summary>
/// An image file (PNG and what Avalonia reads), drawn by the control itself.
/// </summary>
public sealed record FilePicture(string Path) : PreviewPicture
{
    public override object Key => Path;

    public override (int Width, int Height)? GridSize => null;

    public override (uint[] Pixels, int Width, int Height)? Render(int size) => null;
}

/// <summary>
/// A grid of coverage (a mask, a stratum's mask, a layer of a coverage stack): black empty, white full.
/// </summary>
public sealed record CoveragePicture(TiledBuffer<byte> Grid) : PreviewPicture
{
    public override object Key => Grid;

    public override (int Width, int Height)? GridSize => (Grid.Width, Grid.Height);

    public override (uint[] Pixels, int Width, int Height)? Render(int size)
    {
        (int width, int height) = Fit(Grid.Width, Grid.Height, size);
        uint[] pixels = new uint[width * height];
        for (int y = 0; y < height; y++)
        {
            int z = Math.Min(Grid.Height - 1, (int)((y + 0.5) * Grid.Height / height));
            for (int x = 0; x < width; x++)
            {
                uint value = Grid[Math.Min(Grid.Width - 1, (int)((x + 0.5) * Grid.Width / width)), z];
                pixels[y * width + x] = 0xFF000000u | value << 16 | value << 8 | value;
            }
        }
        return (pixels, width, height);
    }
}

/// <summary>
/// Heights in game units (a height layer, the heightmap): absolute heights with the top view's
/// ramp from their lowest to their highest (green low, brown, grey high), lit from the north-west;
/// differences (a layer that adds) grey at 0, lighter up and darker down. Where a coverage says
/// there is none of the layer, a dark checker shows through.
/// </summary>
public sealed record HeightPicture(TiledBuffer<float> Values, bool Differences = false, TiledBuffer<byte>? Coverage = null) : PreviewPicture
{
    public override object Key => Values;

    public override string Variant => (Differences ? "differences" : "heights") + (Coverage is null ? "" : "+" + Coverage.GetHashCode());

    public override (int Width, int Height)? GridSize => (Values.Width, Values.Height);

    public override (uint[] Pixels, int Width, int Height)? Render(int size) =>
        Draw(Values.Width, Values.Height, (x, z) => Values[x, z], Coverage is TiledBuffer<byte> coverage ? (x, z) => coverage[x, z] / 255f : null, Differences, size);

    /// <summary>
    /// Draws heights read from a grid of that size.
    /// </summary>
    internal static (uint[] Pixels, int Width, int Height) Draw(int gridWidth, int gridHeight, Func<int, int, float> valueAt, Func<int, int, float>? weightAt,
        bool differences, int size)
    {
        (int width, int height) = Fit(gridWidth, gridHeight, size);
        float[] heights = new float[width * height];
        float[] covered = new float[width * height];
        float low = float.MaxValue;
        float high = float.MinValue;
        for (int y = 0; y < height; y++)
        {
            int z = Math.Min(gridHeight - 1, (int)((y + 0.5) * gridHeight / height));
            for (int x = 0; x < width; x++)
            {
                int i = Math.Min(gridWidth - 1, (int)((x + 0.5) * gridWidth / width));
                float value = valueAt(i, z);
                float weight = weightAt?.Invoke(i, z) ?? 1;
                heights[y * width + x] = value;
                covered[y * width + x] = weight;
                if (weight > 0)
                {
                    low = Math.Min(low, value);
                    high = Math.Max(high, value);
                }
            }
        }
        if (low > high)
        {
            (low, high) = (0, 0);
        }
        float reach = Math.Max(Math.Max(Math.Abs(low), Math.Abs(high)), 1e-3f);
        float range = Math.Max(high - low, 1e-3f);
        // a step between pixels in game units per pixel, for the light
        float perPixel = (float)gridWidth / width;
        uint[] pixels = new uint[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                float h = heights[i];
                (float r, float g, float b) colour;
                if (differences)
                {
                    float t = Math.Clamp(h / reach, -1, 1);
                    float grey = 128 + 110 * t;
                    colour = (grey, grey, grey);
                }
                else
                {
                    float t = Math.Clamp((h - low) / range, 0, 1);
                    colour = t < 0.5f ? Mix((70, 110, 60), (150, 135, 95), t * 2) : Mix((150, 135, 95), (225, 225, 225), (t - 0.5f) * 2);
                    float slopeX = (heights[y * width + Math.Min(width - 1, x + 1)] - heights[y * width + Math.Max(0, x - 1)]) / (2 * perPixel);
                    float slopeZ = (heights[Math.Min(height - 1, y + 1) * width + x] - heights[Math.Max(0, y - 1) * width + x]) / (2 * perPixel);
                    float length = MathF.Sqrt(slopeX * slopeX + 1 + slopeZ * slopeZ);
                    float light = Math.Clamp((slopeX * 0.5f + 0.7f + slopeZ * 0.5f) / length, 0, 1);
                    float brightness = 0.35f + 0.65f * light;
                    colour = (colour.r * brightness, colour.g * brightness, colour.b * brightness);
                }
                float weight = covered[i];
                if (weight < 1)
                {
                    float checker = ((x / 4 + y / 4) & 1) == 0 ? 40 : 56;
                    colour = Mix((checker, checker, checker), colour, weight);
                }
                pixels[i] = 0xFF000000u
                    | (uint)Math.Clamp(colour.r, 0, 255) << 16
                    | (uint)Math.Clamp(colour.g, 0, 255) << 8
                    | (uint)Math.Clamp(colour.b, 0, 255);
            }
        }
        return (pixels, width, height);
    }

    internal static (float r, float g, float b) Mix((float R, float G, float B) a, (float R, float G, float B) b, float t) =>
        (a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);
}

/// <summary>
/// The heightmap's samples as heights, drawn as a <see cref="HeightPicture"/> of absolute heights.
/// </summary>
public sealed record HeightmapPicture(TiledBuffer<ushort> Samples, float Scale) : PreviewPicture
{
    public override object Key => Samples;

    public override (int Width, int Height)? GridSize => (Samples.Width, Samples.Height);

    public override (uint[] Pixels, int Width, int Height)? Render(int size) =>
        HeightPicture.Draw(Samples.Width, Samples.Height, (x, z) => Samples[x, z] * Scale, null, false, size);
}
