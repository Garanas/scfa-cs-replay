using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Rendering;

/// <summary>
/// A rectangle of samples, inclusive.
/// </summary>
public readonly record struct SampleRect(int X0, int Y0, int X1, int Y1);

/// <summary>
/// The terrain seen from above as colours, one per sample of the heightmap (BGRA, opaque): shaded
/// by a light from the north-west, coloured by height, blue below the water, and with a grid laid
/// over it (<see cref="GridOverlay"/>) while one is being painted: a mask in the theme's mask colour
/// (<see cref="MaskColour"/>), or the terrain's types in their colours. The 2D view shows it.
/// </summary>
/// <remarks>
/// <see cref="Update"/> colours again only what changed since the last update: the tiles of the
/// heightmap or the grid that are no longer shared with the version shown before.
/// </remarks>
public sealed class TerrainPicture
{
    private TiledBuffer<ushort>? _samples;
    private TiledBuffer<byte>? _mask;
    private float _scale;
    private float _water = float.NegativeInfinity;
    private string? _overlay;
    private IReadOnlyList<uint>? _palette;
    private float _low;
    private float _high;
    private uint _maskColourOf;
    private (float R, float G, float B) _maskColour;

    /// <summary>
    /// The colour a mask is laid over the terrain in, <c>0xAARRGGBB</c> (the theme's
    /// <c>MapMaskBrush</c>); another colour colours the whole picture again.
    /// </summary>
    public uint MaskColour { get; set; }

    public int Width { get; private set; }

    public int Height { get; private set; }

    /// <summary>
    /// The colours row by row, <see cref="Width"/> by <see cref="Height"/>.
    /// </summary>
    public uint[] Pixels { get; private set; } = [];

    /// <summary>
    /// Colours what changed since the last update, with the mask of a terrain layer over it.
    /// </summary>
    /// <param name="layer">The terrain layer whose mask to lay over the terrain (1 to 8), or 0 for none.</param>
    public IReadOnlyList<SampleRect> Update(MapDocument document, int layer) =>
        Update(document, layer > 0 ? GridOverlay.Mask(PaintTarget.Stratum(layer)) : null);

    /// <summary>
    /// Colours what changed since the last update.
    /// </summary>
    /// <param name="overlay">The grid to lay over the terrain, or null for none.</param>
    /// <returns>The rectangles that were coloured again; all of the picture when its size or the
    /// way it is coloured changed.</returns>
    public IReadOnlyList<SampleRect> Update(MapDocument document, GridOverlay? overlay)
    {
        Heightmap heightmap = document.Terrain.Heightmap;
        TiledBuffer<ushort> samples = heightmap.Samples;
        float water = document.Water.HasWater ? document.Water.Elevation : float.NegativeInfinity;
        TiledBuffer<byte>? mask = overlay?.Select(document);
        string? key = mask is null ? null : overlay!.Key;

        bool all = ColoursAll(document, overlay);

        List<SampleRect> changed = [];
        if (all)
        {
            Width = samples.Width;
            Height = samples.Height;
            Pixels = new uint[Width * Height];
            _scale = heightmap.Scale;
            _water = water;
            _overlay = key;
            _palette = overlay?.Palette;
            _maskColourOf = MaskColour;
            _maskColour = (MaskColour >> 16 & 0xff, MaskColour >> 8 & 0xff, MaskColour & 0xff);
            ushort[] values = samples.ToArray();
            _low = values.Min() * _scale;
            _high = Math.Max(_low + 1, values.Max() * _scale);
            ColourAll(values, mask?.ToArray(), mask?.Width ?? 0, mask?.Height ?? 0);
            changed.Add(new SampleRect(0, 0, Width - 1, Height - 1));
        }
        else
        {
            if (!ReferenceEquals(samples, _samples))
            {
                foreach ((int tileX, int tileY) in samples.ChangedTiles(_samples!))
                {
                    // the part of the tile whose samples differ (a dab touches a few), one sample more on
                    // each side: the shading of a sample reads its neighbours
                    if (ChangedBox(samples, _samples!, tileX, tileY) is (int bx0, int by0, int bx1, int by1))
                    {
                        int x0 = tileX * TiledBuffer<ushort>.TileSize;
                        int y0 = tileY * TiledBuffer<ushort>.TileSize;
                        changed.Add(Clip(new SampleRect(x0 + bx0 - 1, y0 + by0 - 1, x0 + bx1 + 1, y0 + by1 + 1)));
                    }
                }
            }
            if (mask is not null && !ReferenceEquals(mask, _mask))
            {
                // the samples a tile of the mask covers
                double perPixelX = (Width - 1) / (double)mask.Width;
                double perPixelY = (Height - 1) / (double)mask.Height;
                foreach ((int tileX, int tileY) in mask.ChangedTiles(_mask!))
                {
                    if (ChangedBox(mask, _mask!, tileX, tileY) is not (int bx0, int by0, int bx1, int by1))
                    {
                        continue;
                    }
                    int x0 = tileX * TiledBuffer<byte>.TileSize;
                    int y0 = tileY * TiledBuffer<byte>.TileSize;
                    changed.Add(Clip(new SampleRect(
                        (int)Math.Floor((x0 + bx0) * perPixelX) - 1,
                        (int)Math.Floor((y0 + by0) * perPixelY) - 1,
                        (int)Math.Ceiling((x0 + bx1 + 1) * perPixelX) + 1,
                        (int)Math.Ceiling((y0 + by1 + 1) * perPixelY) + 1)));
                }
            }
            if (changed.Count > ParallelRects)
            {
                // a stratum or mask changed everywhere (a mask layer added): the rows of tiles spread over the processors
                Parallel.ForEach(changed, rect => Colour(samples, mask, rect));
            }
            else
            {
                foreach (SampleRect rect in changed)
                {
                    Colour(samples, mask, rect);
                }
            }
        }

        _samples = samples;
        _mask = mask;
        return changed;
    }

    /// <summary>
    /// From this many rectangles on, <see cref="Update"/> colours them on several processors (they
    /// touch only at their edges, where both write the same colour).
    /// </summary>
    private const int ParallelRects = 8;

    /// <summary>
    /// Whether <see cref="Update"/> of this document colours the whole picture again: its size,
    /// the height's scale, the water or the way it is coloured changed.
    /// </summary>
    public bool ColoursAll(MapDocument document, GridOverlay? overlay)
    {
        TiledBuffer<ushort> samples = document.Terrain.Heightmap.Samples;
        float water = document.Water.HasWater ? document.Water.Elevation : float.NegativeInfinity;
        TiledBuffer<byte>? mask = overlay?.Select(document);
        string? key = mask is null ? null : overlay!.Key;
        return _samples is null
            || samples.Width != Width
            || samples.Height != Height
            || document.Terrain.Heightmap.Scale != _scale
            || water != _water
            || key != _overlay
            || (mask is not null && MaskColour != _maskColourOf)
            || (mask is not null && (_mask is null || mask.Width != _mask.Width || mask.Height != _mask.Height));
    }

    /// <summary>
    /// The columns and rows of a tile (inclusive, within the tile) where two versions of a grid
    /// differ; null when they hold the same values there. Grids of another size differ everywhere.
    /// </summary>
    internal static (int X0, int Y0, int X1, int Y1)? ChangedBox<T>(TiledBuffer<T> now, TiledBuffer<T> before, int tileX, int tileY)
        where T : unmanaged, IEquatable<T>
    {
        const int Size = TiledBuffer<T>.TileSize;
        int columns = Math.Min(Size, now.Width - tileX * Size);
        int rows = Math.Min(Size, now.Height - tileY * Size);
        if (before.Width != now.Width || before.Height != now.Height)
        {
            return (0, 0, columns - 1, rows - 1);
        }
        ReadOnlySpan<T> a = now.Tile(tileX, tileY);
        ReadOnlySpan<T> b = before.Tile(tileX, tileY);
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
        for (int row = 0; row < rows; row++)
        {
            ReadOnlySpan<T> left = a.Slice(row * Size, columns);
            ReadOnlySpan<T> right = b.Slice(row * Size, columns);
            int first = left.CommonPrefixLength(right);
            if (first == columns)
            {
                continue;
            }
            int last = columns - 1;
            while (last > first && left[last].Equals(right[last]))
            {
                last--;
            }
            x0 = Math.Min(x0, first);
            x1 = Math.Max(x1, last);
            y0 = Math.Min(y0, row);
            y1 = row;
        }
        return x1 < 0 ? null : (x0, y0, x1, y1);
    }

    private SampleRect Clip(SampleRect rect) => new SampleRect(
        Math.Max(0, rect.X0), Math.Max(0, rect.Y0), Math.Min(Width - 1, rect.X1), Math.Min(Height - 1, rect.Y1));

    /// <summary>
    /// Colours every sample from flat arrays, the rows spread over the processors: a 4097 by 4097
    /// heightmap is 16.8 million samples.
    /// </summary>
    private void ColourAll(ushort[] values, byte[]? mask, int maskWidth, int maskHeight)
    {
        int width = Width;
        int height = Height;
        uint[] pixels = Pixels;
        Parallel.For(0, height, y =>
        {
            int above = Math.Max(0, y - 1) * width;
            int here = y * width;
            int below = Math.Min(height - 1, y + 1) * width;
            int maskRow = mask is null ? 0 : Math.Min(maskHeight - 1, (int)((long)y * maskHeight / (height - 1))) * maskWidth;
            for (int x = 0; x < width; x++)
            {
                int left = Math.Max(0, x - 1);
                int right = Math.Min(width - 1, x + 1);
                float slopeX = (values[here + right] - values[here + left]) * _scale / 2;
                float slopeZ = (values[below + x] - values[above + x]) * _scale / 2;
                int value = mask is null ? -1 : mask[maskRow + Math.Min(maskWidth - 1, (int)((long)x * maskWidth / (width - 1)))];
                pixels[here + x] = Shade(values[here + x] * _scale, slopeX, slopeZ, value);
            }
        });
    }

    /// <summary>
    /// Colours the samples of a rectangle.
    /// </summary>
    private void Colour(TiledBuffer<ushort> samples, TiledBuffer<byte>? mask, SampleRect rect)
    {
        int width = Width;
        int height = Height;
        for (int y = rect.Y0; y <= rect.Y1; y++)
        {
            for (int x = rect.X0; x <= rect.X1; x++)
            {
                float h = samples[x, y] * _scale;
                float west = samples[Math.Max(0, x - 1), y] * _scale;
                float east = samples[Math.Min(width - 1, x + 1), y] * _scale;
                float north = samples[x, Math.Max(0, y - 1)] * _scale;
                float south = samples[x, Math.Min(height - 1, y + 1)] * _scale;
                int value = mask is null
                    ? -1
                    : mask[Math.Min(mask.Width - 1, (int)((long)x * mask.Width / (width - 1))), Math.Min(mask.Height - 1, (int)((long)y * mask.Height / (height - 1)))];
                Pixels[y * width + x] = Shade(h, (east - west) / 2, (south - north) / 2, value);
            }
        }
    }

    /// <summary>
    /// The colour of a sample: by height (green low, brown, grey high), blue under water, the
    /// overlay's colour where its grid has a value, darker where the slope faces away from a light
    /// in the north-west.
    /// </summary>
    /// <param name="value">The overlay's grid there, or -1 without one.</param>
    private uint Shade(float height, float slopeX, float slopeZ, int value)
    {
        // the normal is (-slopeX, 1, -slopeZ); the light comes from (-1, 1.4, -1)
        const float LightX = -0.5f, LightY = 0.7f, LightZ = -0.5f;
        float length = MathF.Sqrt(slopeX * slopeX + 1 + slopeZ * slopeZ);
        float light = Math.Clamp((-slopeX * LightX + LightY - slopeZ * LightZ) / (length * 0.995f), 0, 1);

        float t = Math.Clamp((height - _low) / (_high - _low), 0, 1);
        (float r, float g, float b) = t < 0.5f
            ? Mix((70, 110, 60), (150, 135, 95), t * 2)
            : Mix((150, 135, 95), (225, 225, 225), (t - 0.5f) * 2);
        if (height < _water)
        {
            float depth = Math.Clamp((_water - height) / 20, 0, 1);
            (r, g, b) = Mix((r, g, b), Mix((50, 110, 160), (20, 45, 90), depth), 0.75f);
        }
        if (value > 0 && _palette is null)
        {
            (r, g, b) = Mix((r, g, b), _maskColour, value / 255f * 0.7f);
        }
        else if (value >= 0 && _palette is not null && _palette[value] >> 24 != 0)
        {
            uint colour = _palette[value];
            (r, g, b) = Mix((r, g, b), (colour >> 16 & 0xff, colour >> 8 & 0xff, colour & 0xff), 0.75f);
        }

        float brightness = 0.3f + 0.7f * light;
        return 0xFF000000u
            | (uint)Math.Clamp(r * brightness, 0, 255) << 16
            | (uint)Math.Clamp(g * brightness, 0, 255) << 8
            | (uint)Math.Clamp(b * brightness, 0, 255);
    }

    private static (float, float, float) Mix((float R, float G, float B) a, (float R, float G, float B) b, float t) =>
        (a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);
}
