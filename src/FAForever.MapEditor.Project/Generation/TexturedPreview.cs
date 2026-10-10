using System.Numerics;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.Project.Generation
{
    /// <summary>
    /// The map's preview drawn with the strata's textures (user story S-4): from straight above,
    /// each pixel the layers blended by their masks as the terrain shader blends them
    /// (<see cref="TerrainShaderCatalog"/>: four or eight strata, the whole or the upper half of a
    /// mask, stratum 8 and the upper layer as textures or as data), lit by the map's sun as the 3D
    /// view lights it (<c>CalculateXPLighting</c> without the specular part), and under the water
    /// tinted by its surface colour down to the abyss. A texture is read once and made smaller in
    /// steps of two, and a pixel takes the step whose texels are as large as the ground it covers,
    /// so a pixel shows the colour of what it covers, not one texel of it.
    /// </summary>
    public static class TexturedPreview
    {
        /// <summary>
        /// The colour a layer is drawn in when its texture cannot be read: a neutral grey, so the
        /// light and water still show.
        /// </summary>
        private static readonly Vector3 Fallback = new Vector3(0.5f);

        /// <summary>
        /// Draws the preview, <paramref name="size"/> pixels square, opaque.
        /// </summary>
        /// <param name="texture">The texture of a layer's albedo path, or null when there is none.</param>
        public static TiledBuffer<Color32> Render(MapDocument document, Func<string, DecodedTexture?> texture, int size = 256)
        {
            Terrain terrain = document.Terrain;
            TerrainShaderInfo shader = TerrainShaderCatalog.Of(terrain.Shader);
            int last = terrain.Layers.Length - 1;
            float perPixel = Math.Max(document.Width, document.Height) / (float)size;

            // the layers in the order they blend: the lower layer, the strata, the upper layer
            Layer?[] layers = new Layer?[terrain.Layers.Length];
            for (int i = 0; i <= last; i++)
            {
                bool stratum = i > 0 && i < last;
                if ((stratum && i > shader.Strata) || (stratum && i == 8 && shader.Stratum8IsData) || (i == last && last > 0 && !shader.UpperIsAlbedo))
                {
                    continue;
                }
                TerrainLayer layer = terrain.Layers[i];
                if (string.IsNullOrWhiteSpace(layer.AlbedoPath))
                {
                    continue;
                }
                DecodedTexture? decoded = null;
                try
                {
                    decoded = texture(layer.AlbedoPath);
                }
                catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ImageMagick.MagickException)
                {
                    // drawn grey, as a texture the workspace lacks
                }
                float scale = layer.AlbedoScale > 0 ? layer.AlbedoScale : 4;
                // an upper layer that cannot be read covers nothing, as in the 3D view
                layers[i] = new Layer(decoded is null ? null : Mips.Of(decoded), scale, perPixel, i == last && last > 0 ? 0 : 1);
            }

            Heightmap heightmap = terrain.Heightmap;
            Lighting lighting = document.Lighting;
            Vector3 sun = lighting.SunDirection.LengthSquared() > 0 ? Vector3.Normalize(lighting.SunDirection) : Vector3.UnitY;
            Water water = document.Water;
            Vector3 surface = Vector3.Clamp(water.SurfaceColor, Vector3.Zero, Vector3.One);

            Color32[] pixels = new Color32[size * size];
            Parallel.For(0, size, row =>
            {
                Span<float> weights = stackalloc float[layers.Length];
                for (int column = 0; column < size; column++)
                {
                    // the middle of the pixel, in ogrids; a map that is not square leaves a margin
                    float x = (column + 0.5f) * perPixel;
                    float z = (row + 0.5f) * perPixel;
                    if (x > document.Width || z > document.Height)
                    {
                        pixels[row * size + column] = new Color32(255, 0, 0, 0);
                        continue;
                    }
                    Vector3 albedo = Albedo(layers, terrain.StratumMasks, x / document.Width, z / document.Height, x, z, shader.FullMaskRange, last);

                    float height = heightmap.HeightAt(x, z);
                    float step = Math.Max(1, perPixel / 2);
                    float west = heightmap.HeightAt(Math.Max(0, x - step), z);
                    float east = heightmap.HeightAt(Math.Min(document.Width, x + step), z);
                    float north = heightmap.HeightAt(x, Math.Max(0, z - step));
                    float south = heightmap.HeightAt(x, Math.Min(document.Height, z + step));
                    Vector3 normal = Vector3.Normalize(new Vector3(west - east, 2 * step, north - south));
                    Vector3 light = lighting.SunColor * Math.Max(Vector3.Dot(sun, normal), 0) + lighting.SunAmbience;
                    light = lighting.Multiplier * light + lighting.ShadowFillColor * (Vector3.One - light);
                    Vector3 colour = albedo * light;

                    if (water.HasWater && height < water.Elevation)
                    {
                        // as the 3D view's water: darker and more opaque with depth, down to the abyss
                        float t = Math.Clamp((water.Elevation - height) / Math.Max(water.Elevation - water.ElevationAbyss, 1), 0, 1);
                        Vector3 tint = Vector3.Lerp(surface * 0.55f, surface * 0.12f, t);
                        float alpha = Math.Clamp(0.25f + 0.75f * MathF.Sqrt(t), 0, 0.92f);
                        colour = tint * alpha + colour * (1 - alpha);
                    }
                    pixels[row * size + column] = new Color32(255, Byte(colour.X), Byte(colour.Y), Byte(colour.Z));
                }
            });
            return TiledBuffer<Color32>.FromArray(size, size, pixels);
        }

        /// <summary>
        /// The layers blended at a place: the lower layer, each stratum over it by its mask, the
        /// upper layer by its alpha.
        /// </summary>
        private static Vector3 Albedo(Layer?[] layers, StratumMasks masks, float u, float v, float x, float z, bool fullMaskRange, int last)
        {
            Vector3 albedo = layers.Length > 0 && layers[0] is Layer lower ? lower.Sample(x, z).Rgb : Fallback;
            for (int i = 1; i < last && i - 1 < masks.Layers.Length; i++)
            {
                if (layers[i] is not Layer stratum)
                {
                    continue;
                }
                TiledBuffer<byte> mask = masks.Layers[i - 1];
                float value = MaskAt(mask, u, v);
                float weight = fullMaskRange ? value : Math.Clamp(value * 2 - 1, 0, 1);
                if (weight > 0)
                {
                    albedo = Vector3.Lerp(albedo, stratum.Sample(x, z).Rgb, weight);
                }
            }
            if (last > 0 && layers[last] is Layer upper)
            {
                (Vector3 rgb, float alpha) = upper.Sample(x, z);
                albedo = Vector3.Lerp(albedo, rgb, alpha);
            }
            return albedo;
        }

        /// <summary>
        /// A mask's value at a place of the map (0 to 1 across it), interpolated, 0 to 1.
        /// </summary>
        private static float MaskAt(TiledBuffer<byte> mask, float u, float v)
        {
            float px = Math.Clamp(u * mask.Width - 0.5f, 0, mask.Width - 1);
            float py = Math.Clamp(v * mask.Height - 0.5f, 0, mask.Height - 1);
            int x0 = (int)px;
            int y0 = (int)py;
            int x1 = Math.Min(mask.Width - 1, x0 + 1);
            int y1 = Math.Min(mask.Height - 1, y0 + 1);
            float fx = px - x0;
            float fy = py - y0;
            float top = mask[x0, y0] + (mask[x1, y0] - mask[x0, y0]) * fx;
            float bottom = mask[x0, y1] + (mask[x1, y1] - mask[x0, y1]) * fx;
            return (top + (bottom - top) * fy) / 255f;
        }

        private static byte Byte(float value) => (byte)Math.Clamp(MathF.Round(value * 255), 0, 255);

        /// <summary>
        /// A layer's texture, with how often it repeats and the step of its mips a pixel takes.
        /// </summary>
        private sealed class Layer(Mips? mips, float scale, float perPixel, float fallbackAlpha)
        {
            // the step whose texels cover as much ground as a pixel: a texel covers scale / width ogrids
            private readonly int _level = mips is null ? 0 : mips.LevelFor(perPixel * mips.Width / scale);

            public (Vector3 Rgb, float Alpha) Sample(float x, float z) =>
                mips is null ? (Fallback, fallbackAlpha) : mips.Sample(_level, x / scale, z / scale);
        }

        /// <summary>
        /// A texture and its halvings, as colours of 0 to 1, repeating.
        /// </summary>
        private sealed class Mips
        {
            // the first step kept is at most this wide: a preview never needs more of a texture
            private const int MaxWidth = 256;

            private readonly List<(int Width, int Height, Vector4[] Texels)> _levels = [];

            // the steps of two left out before the first kept: the full texture is 2^shift as wide
            private int _shift;

            /// <summary>
            /// The width of the full texture.
            /// </summary>
            public int Width => _levels[0].Width << _shift;

            public static Mips Of(DecodedTexture texture)
            {
                Mips mips = new Mips();
                int factor = 1;
                while (texture.Width / factor > MaxWidth || texture.Height / factor > MaxWidth)
                {
                    factor *= 2;
                    mips._shift++;
                }
                int width = Math.Max(1, texture.Width / factor);
                int height = Math.Max(1, texture.Height / factor);
                Vector4[] texels = new Vector4[width * height];
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        Vector4 sum = Vector4.Zero;
                        for (int dy = 0; dy < factor; dy++)
                        {
                            int source = ((y * factor + dy) * texture.Width + x * factor) * 4;
                            for (int dx = 0; dx < factor; dx++, source += 4)
                            {
                                sum += new Vector4(texture.Rgba[source], texture.Rgba[source + 1], texture.Rgba[source + 2], texture.Rgba[source + 3]);
                            }
                        }
                        texels[y * width + x] = sum / (255f * factor * factor);
                    }
                }
                mips._levels.Add((width, height, texels));
                while (width > 1 || height > 1)
                {
                    int halfWidth = Math.Max(1, width / 2);
                    int halfHeight = Math.Max(1, height / 2);
                    Vector4[] half = new Vector4[halfWidth * halfHeight];
                    for (int y = 0; y < halfHeight; y++)
                    {
                        for (int x = 0; x < halfWidth; x++)
                        {
                            int x0 = Math.Min(width - 1, x * 2);
                            int y0 = Math.Min(height - 1, y * 2);
                            int x1 = Math.Min(width - 1, x0 + 1);
                            int y1 = Math.Min(height - 1, y0 + 1);
                            half[y * halfWidth + x] = (texels[y0 * width + x0] + texels[y0 * width + x1] + texels[y1 * width + x0] + texels[y1 * width + x1]) / 4;
                        }
                    }
                    texels = half;
                    width = halfWidth;
                    height = halfHeight;
                    mips._levels.Add((width, height, texels));
                }
                return mips;
            }

            /// <summary>
            /// The step whose texel is about <paramref name="texels"/> texels of the full texture wide.
            /// </summary>
            public int LevelFor(float texels) =>
                texels <= 1 ? 0 : Math.Clamp((int)MathF.Round(MathF.Log2(texels)) - _shift, 0, _levels.Count - 1);

            /// <summary>
            /// The colour at a place of the texture (1 is once across), interpolated, repeating.
            /// </summary>
            public (Vector3, float) Sample(int level, float u, float v)
            {
                (int width, int height, Vector4[] texels) = _levels[level];
                float px = (u - MathF.Floor(u)) * width - 0.5f;
                float py = (v - MathF.Floor(v)) * height - 0.5f;
                int x0 = (int)MathF.Floor(px);
                int y0 = (int)MathF.Floor(py);
                float fx = px - x0;
                float fy = py - y0;
                int Wrap(int i, int n) => ((i % n) + n) % n;
                int xa = Wrap(x0, width), xb = Wrap(x0 + 1, width), ya = Wrap(y0, height), yb = Wrap(y0 + 1, height);
                Vector4 top = Vector4.Lerp(texels[ya * width + xa], texels[ya * width + xb], fx);
                Vector4 bottom = Vector4.Lerp(texels[yb * width + xa], texels[yb * width + xb], fx);
                Vector4 texel = Vector4.Lerp(top, bottom, fy);
                return (new Vector3(texel.X, texel.Y, texel.Z), texel.W);
            }
        }
    }
}
