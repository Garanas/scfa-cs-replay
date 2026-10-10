using System.Collections.Immutable;
using System.Globalization;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// Adds the height layers of a map up to its heightmap (user story T-5), and keeps the heightmap
    /// that sum after every change (<see cref="Follow"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per corner the heights start at 0 and each visible layer, lowest first, works on them with
    /// its weight w (strength × mask × coverage) and value v: <see cref="HeightCombine.Add"/> adds
    /// w × v, <see cref="HeightCombine.Maximum"/> moves the height w of the way up to v when v is
    /// higher, <see cref="HeightCombine.Minimum"/> down when lower, <see cref="HeightCombine.Replace"/>
    /// w of the way to v. The base replaces at full weight, so a stack of only the base is the
    /// heightmap sample for sample.
    /// </para>
    /// <para>
    /// The layers' values share the heightmap's tiles: a change of a layer, or of a mask a layer
    /// uses, adds up again only the tiles it touched. A change of the heightmap itself while there
    /// are layers (a brush, an import) goes into the sculpting layer the editor chose
    /// (<see cref="EditSession.SculptLayer"/>), so that the sum is what the brush made; without one
    /// the sum wins and the change is gone.
    /// </para>
    /// </remarks>
    public static class HeightLayers
    {
        /// <summary>
        /// How a layer names a mask for tools: <c>mask:rivers</c>.
        /// </summary>
        public const string MaskPrefix = "mask:";

        /// <summary>
        /// How a layer names the mask of a terrain layer: <c>stratum:3</c>.
        /// </summary>
        public const string StratumPrefix = "stratum:";

        private const int Size = TiledBuffer<float>.TileSize;

        /// <summary>
        /// The key of a mask for tools, for <see cref="HeightLayer.Mask"/>.
        /// </summary>
        public static string MaskKey(string name) => MaskPrefix + name;

        /// <summary>
        /// The key of the mask of terrain layer <paramref name="layer"/> (1 to 8).
        /// </summary>
        public static string StratumKey(int layer) => StratumPrefix + layer.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// What a mask key names, for people: <c>mask rivers</c>, <c>stratum 3</c>.
        /// </summary>
        public static string MaskTitle(string key) =>
            key.StartsWith(MaskPrefix, StringComparison.Ordinal) ? $"mask {key[MaskPrefix.Length..]}"
            : key.StartsWith(StratumPrefix, StringComparison.Ordinal) ? $"stratum {key[StratumPrefix.Length..]}"
            : key;

        /// <summary>
        /// The mask a key names in a document; null for no key, or a mask the document lacks.
        /// </summary>
        public static TiledBuffer<byte>? MaskOf(MapDocument document, string? key)
        {
            if (key is null)
            {
                return null;
            }
            if (key.StartsWith(MaskPrefix, StringComparison.Ordinal))
            {
                return document.Masks.TryGetValue(key[MaskPrefix.Length..], out TiledBuffer<byte>? mask) ? mask : null;
            }
            if (key.StartsWith(StratumPrefix, StringComparison.Ordinal)
                && int.TryParse(key[StratumPrefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out int layer)
                && layer >= 1 && layer <= document.Terrain.StratumMasks.Layers.Length)
            {
                return document.Terrain.StratumMasks.Layers[layer - 1];
            }
            return null;
        }

        /// <summary>
        /// The layer of that name (exactly, else without regard to case), or null.
        /// </summary>
        public static HeightLayer? Find(MapDocument document, string? name) =>
            name is null ? null
            : document.HeightLayers.FirstOrDefault(layer => layer.Name == name)
                ?? document.HeightLayers.FirstOrDefault(layer => layer.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Why the sculpt tool cannot write into a layer, or null when it can (or the map has no
        /// layers: then it writes the heightmap).
        /// </summary>
        public static string? SculptProblem(MapDocument document, string? layerName)
        {
            if (document.HeightLayers.IsEmpty)
            {
                return null;
            }
            if (Find(document, layerName) is not HeightLayer layer)
            {
                return "Choose a height layer to sculpt into";
            }
            if (layer.Locked)
            {
                return $"The height layer {layer.Name} is locked";
            }
            if (!layer.Visible)
            {
                return $"The height layer {layer.Name} is hidden: show it to sculpt into it";
            }
            return layer.Kind switch
            {
                HeightLayerKind.Curve => $"The height layer {layer.Name} follows its curve: edit the curve, or sculpt into another layer",
                HeightLayerKind.Imported => $"The height layer {layer.Name} is read from {layer.Source ?? "its file"}: sculpt into a layer above it",
                _ => null,
            };
        }

        /// <summary>
        /// The document with its heightmap the sum of its layers, every tile added up again.
        /// </summary>
        public static MapDocument Recomposite(MapDocument document)
        {
            if (document.HeightLayers.IsEmpty)
            {
                return document;
            }
            TiledBuffer<ushort> samples = document.Terrain.Heightmap.Samples;
            bool[] dirty = new bool[samples.TilesX * samples.TilesY];
            Array.Fill(dirty, true);
            return WithComposite(document, dirty);
        }

        /// <summary>
        /// The document after a change, with its heightmap the sum of its layers again: the tiles
        /// whose layers or masks changed are added up again, and a change of the heightmap itself
        /// goes into the layer <paramref name="sculptLayer"/> first (or is undone without one that
        /// can be sculpted, <see cref="SculptProblem"/>).
        /// </summary>
        /// <param name="previous">The document before the change, whose heightmap was the sum of
        /// its layers.</param>
        /// <returns><paramref name="next"/> itself when it holds the sum already.</returns>
        public static MapDocument Follow(MapDocument previous, MapDocument next, string? sculptLayer)
        {
            if (next.HeightLayers.IsEmpty)
            {
                return next;
            }
            TiledBuffer<ushort> before = previous.Terrain.Heightmap.Samples;
            TiledBuffer<ushort> samples = next.Terrain.Heightmap.Samples;
            bool sameStack = previous.HeightLayers == next.HeightLayers;
            bool sameMasks = ReferenceEquals(previous.Masks, next.Masks) && ReferenceEquals(previous.Terrain.StratumMasks, next.Terrain.StratumMasks);
            if (sameStack && sameMasks && ReferenceEquals(before, samples))
            {
                return next;
            }

            MapDocument result = FollowRenamedMasks(previous, next);
            int tilesX = samples.TilesX;
            bool[] dirty = new bool[tilesX * samples.TilesY];
            bool sameSize = before.Width == samples.Width && before.Height == samples.Height;
            if (!sameSize || !SameSettings(previous, result))
            {
                Array.Fill(dirty, true);
            }
            else
            {
                for (int i = 0; i < result.HeightLayers.Length; i++)
                {
                    HeightLayer was = previous.HeightLayers[i];
                    HeightLayer layer = result.HeightLayers[i];
                    if (ReferenceEquals(was, layer))
                    {
                        continue;
                    }
                    Mark(dirty, tilesX, layer.Values.ChangedTiles(was.Values));
                    if (layer.Coverage is not null && was.Coverage is not null)
                    {
                        Mark(dirty, tilesX, layer.Coverage.ChangedTiles(was.Coverage));
                    }
                }
                if (!sameMasks)
                {
                    foreach (string key in result.HeightLayers.Select(layer => layer.Mask).OfType<string>().Distinct())
                    {
                        MarkMask(dirty, samples, MaskOf(previous, key), MaskOf(result, key));
                    }
                }
            }

            if (sameSize && !ReferenceEquals(before, samples))
            {
                result = TakeHeightmapChanges(result, before, dirty, sculptLayer);
            }
            result = WithComposite(result, dirty);

            // a sum that came out as it was is the same heightmap, so a change that changed nothing is no step
            TiledBuffer<ushort> sum = result.Terrain.Heightmap.Samples;
            if (sameSize && !ReferenceEquals(sum, before)
                && sum.ChangedTiles(before).All(tile => sum.Tile(tile.X, tile.Y).SequenceEqual(before.Tile(tile.X, tile.Y))))
            {
                result = result with { Terrain = result.Terrain with { Heightmap = result.Terrain.Heightmap with { Samples = before } } };
            }
            return result;
        }

        /// <summary>
        /// The sum of the layers at every corner of one tile, before the layer <paramref name="upTo"/>
        /// (all of them when it is the count), in game units.
        /// </summary>
        internal static void Heights(MapDocument document, int tileX, int tileY, int upTo, Span<float> heights)
        {
            Source[] sources = Resolve(document);
            Span<float> weights = stackalloc float[Size * Size];
            Heights(sources.AsSpan(0, upTo), document.Terrain.Heightmap.Samples, tileX, tileY, heights, weights);
        }

        /// <summary>
        /// A mask's value at a corner of the map, 0 to 1, between the centres of its pixels: a mask
        /// of any size (the map's, half of it, the stratum masks') covers the whole map.
        /// </summary>
        public static float MaskAt(TiledBuffer<byte> mask, int mapWidth, int mapHeight, int x, int z)
        {
            float u = x * (float)mask.Width / Math.Max(1, mapWidth) - 0.5f;
            float v = z * (float)mask.Height / Math.Max(1, mapHeight) - 0.5f;
            int i = (int)MathF.Floor(u);
            int j = (int)MathF.Floor(v);
            float a = u - i;
            float b = v - j;
            int i0 = Math.Clamp(i, 0, mask.Width - 1);
            int i1 = Math.Clamp(i + 1, 0, mask.Width - 1);
            int j0 = Math.Clamp(j, 0, mask.Height - 1);
            int j1 = Math.Clamp(j + 1, 0, mask.Height - 1);
            float top = mask[i0, j0] * (1 - a) + mask[i1, j0] * a;
            float bottom = mask[i0, j1] * (1 - a) + mask[i1, j1] * a;
            return (top * (1 - b) + bottom * b) / 255f;
        }

        /// <summary>
        /// A layer and the mask it uses, looked up once for adding up many tiles.
        /// </summary>
        private readonly record struct Source(HeightLayer Layer, TiledBuffer<byte>? Mask);

        private static Source[] Resolve(MapDocument document) =>
            [.. document.HeightLayers.Select(layer => new Source(layer, MaskOf(document, layer.Mask)))];

        /// <summary>
        /// Adds up the layers over one tile.
        /// </summary>
        private static void Heights(ReadOnlySpan<Source> sources, TiledBuffer<ushort> samples, int tileX, int tileY, Span<float> heights, Span<float> weights)
        {
            heights.Clear();
            (int columns, int rows) = Extent(samples, tileX, tileY);
            foreach (Source source in sources)
            {
                HeightLayer layer = source.Layer;
                if (!layer.Visible || layer.Strength <= 0)
                {
                    continue;
                }
                Weights(source, samples, tileX, tileY, columns, rows, weights);
                ReadOnlySpan<float> values = layer.Values.Tile(tileX, tileY);
                for (int row = 0; row < rows; row++)
                {
                    for (int column = 0; column < columns; column++)
                    {
                        int i = row * Size + column;
                        float w = weights[i];
                        if (w <= 0)
                        {
                            continue;
                        }
                        float v = values[i];
                        float h = heights[i];
                        heights[i] = layer.Combine switch
                        {
                            HeightCombine.Add => h + w * v,
                            HeightCombine.Maximum => v > h ? h + w * (v - h) : h,
                            HeightCombine.Minimum => v < h ? h + w * (v - h) : h,
                            _ => h + w * (v - h),
                        };
                    }
                }
            }
        }

        /// <summary>
        /// A layer's weight at each corner of a tile: strength × mask × coverage.
        /// </summary>
        private static void Weights(Source source, TiledBuffer<ushort> samples, int tileX, int tileY, int columns, int rows, Span<float> weights)
        {
            HeightLayer layer = source.Layer;
            weights.Fill(Math.Clamp(layer.Strength, 0, 1));
            if (layer.Coverage is TiledBuffer<byte> coverage)
            {
                ReadOnlySpan<byte> covered = coverage.Tile(tileX, tileY);
                for (int i = 0; i < weights.Length; i++)
                {
                    weights[i] *= covered[i] / 255f;
                }
            }
            if (source.Mask is TiledBuffer<byte> mask)
            {
                int mapWidth = samples.Width - 1;
                int mapHeight = samples.Height - 1;
                int x0 = tileX * Size;
                int z0 = tileY * Size;
                for (int row = 0; row < rows; row++)
                {
                    for (int column = 0; column < columns; column++)
                    {
                        int i = row * Size + column;
                        if (weights[i] > 0)
                        {
                            weights[i] *= MaskAt(mask, mapWidth, mapHeight, x0 + column, z0 + row);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Whether two tiles of a buffer's size hold the same values inside the buffer (the part of an
        /// edge tile outside it does not count).
        /// </summary>
        internal static bool SameTile<TSize, T>(TiledBuffer<TSize> size, int tileX, int tileY, ReadOnlySpan<T> a, ReadOnlySpan<T> b)
            where TSize : unmanaged where T : unmanaged, IEquatable<T>
        {
            (int columns, int rows) = Extent(size, tileX, tileY);
            for (int row = 0; row < rows; row++)
            {
                if (!a.Slice(row * Size, columns).SequenceEqual(b.Slice(row * Size, columns)))
                {
                    return false;
                }
            }
            return true;
        }

        private static (int Columns, int Rows) Extent<T>(TiledBuffer<T> buffer, int tileX, int tileY) where T : unmanaged =>
            (Math.Min(Size, buffer.Width - tileX * Size), Math.Min(Size, buffer.Height - tileY * Size));

        private static ushort ToSample(float height, float scale) =>
            (ushort)Math.Clamp(MathF.Round(height / scale), 0, ushort.MaxValue);

        /// <summary>
        /// The document with the dirty tiles of its heightmap added up again; tiles that come out as
        /// they were stay shared.
        /// </summary>
        private static MapDocument WithComposite(MapDocument document, bool[] dirty)
        {
            Heightmap heightmap = document.Terrain.Heightmap;
            TiledBuffer<ushort> samples = heightmap.Samples;
            int tilesX = samples.TilesX;
            Source[] sources = Resolve(document);
            ushort[]?[] results = new ushort[dirty.Length][];
            Parallel.For(0, dirty.Length, () => (Heights: new float[Size * Size], Weights: new float[Size * Size]), (t, _, buffers) =>
            {
                if (!dirty[t])
                {
                    return buffers;
                }
                int tileX = t % tilesX;
                int tileY = t / tilesX;
                Heights(sources, samples, tileX, tileY, buffers.Heights, buffers.Weights);
                ReadOnlySpan<ushort> current = samples.Tile(tileX, tileY);
                (int columns, int rows) = Extent(samples, tileX, tileY);
                ushort[]? tile = null;
                for (int row = 0; row < rows; row++)
                {
                    for (int column = 0; column < columns; column++)
                    {
                        int i = row * Size + column;
                        ushort sample = ToSample(buffers.Heights[i], heightmap.Scale);
                        if (sample != current[i])
                        {
                            tile ??= current.ToArray();
                            tile[i] = sample;
                        }
                    }
                }
                results[t] = tile;
                return buffers;
            }, _ => { });

            TiledBuffer<ushort>.Builder? builder = null;
            for (int t = 0; t < results.Length; t++)
            {
                if (results[t] is ushort[] tile)
                {
                    builder ??= samples.ToBuilder();
                    builder.SetTile(t % tilesX, t / tilesX, tile);
                }
            }
            if (builder is null)
            {
                return document;
            }
            return document with { Terrain = document.Terrain with { Heightmap = heightmap with { Samples = builder.ToBuffer() } } };
        }

        /// <summary>
        /// Puts the changes of the heightmap itself (tiles that are not the sum of the layers) into
        /// the sculpting layer, and marks those tiles to be added up again.
        /// </summary>
        private static MapDocument TakeHeightmapChanges(MapDocument document, TiledBuffer<ushort> before, bool[] dirty, string? sculptLayer)
        {
            TiledBuffer<ushort> samples = document.Terrain.Heightmap.Samples;
            float scale = document.Terrain.Heightmap.Scale;
            Source[] sources = Resolve(document);
            float[] heights = new float[Size * Size];
            float[] weights = new float[Size * Size];
            List<(int X, int Y)> changed = [];
            foreach ((int tileX, int tileY) in samples.ChangedTiles(before))
            {
                Heights(sources, samples, tileX, tileY, heights, weights);
                ReadOnlySpan<ushort> held = samples.Tile(tileX, tileY);
                (int columns, int rows) = Extent(samples, tileX, tileY);
                bool sum = true;
                for (int row = 0; row < rows && sum; row++)
                {
                    for (int column = 0; column < columns; column++)
                    {
                        int i = row * Size + column;
                        if (ToSample(heights[i], scale) != held[i])
                        {
                            sum = false;
                            break;
                        }
                    }
                }
                if (!sum)
                {
                    changed.Add((tileX, tileY));
                    dirty[tileY * samples.TilesX + tileX] = true;
                }
            }
            int target = SculptProblem(document, sculptLayer) is null && Find(document, sculptLayer) is HeightLayer found
                ? document.HeightLayers.IndexOf(found)
                : -1;
            if (changed.Count == 0 || target < 0)
            {
                return document;
            }
            // the brush works on the sum; the layer gets what makes the sum what the brush made

            HeightLayer layer = document.HeightLayers[target];
            TiledBuffer<float>.Builder values = layer.Values.ToBuilder();
            TiledBuffer<byte>.Builder? coverage = layer.Coverage?.ToBuilder();
            float limit = ushort.MaxValue * scale * 2;
            int mapWidth = samples.Width - 1;
            int mapHeight = samples.Height - 1;
            TiledBuffer<byte>? mask = MaskOf(document, layer.Mask);
            float strength = Math.Clamp(layer.Strength, 0, 1);
            foreach ((int tileX, int tileY) in changed)
            {
                Heights(sources.AsSpan(0, target), samples, tileX, tileY, heights, weights);
                ReadOnlySpan<ushort> now = samples.Tile(tileX, tileY);
                ReadOnlySpan<ushort> was = before.Tile(tileX, tileY);
                (int columns, int rows) = Extent(samples, tileX, tileY);
                for (int row = 0; row < rows; row++)
                {
                    for (int column = 0; column < columns; column++)
                    {
                        int i = row * Size + column;
                        if (now[i] == was[i])
                        {
                            continue;
                        }
                        int x = tileX * Size + column;
                        int z = tileY * Size + row;
                        float w = strength * (mask is null ? 1 : MaskAt(mask, mapWidth, mapHeight, x, z));
                        float below = heights[i];
                        float v = values[x, z];
                        if (coverage is not null)
                        {
                            byte covered = coverage[x, z];
                            if (covered == 0 && w > 0)
                            {
                                // a part of a replacing layer not sculpted before starts from the height below
                                coverage[x, z] = 255;
                                v = layer.Combine == HeightCombine.Add ? 0 : below;
                            }
                            else
                            {
                                w *= covered / 255f;
                            }
                        }
                        if (w < 1e-3f)
                        {
                            continue;
                        }
                        float delta = (now[i] - was[i]) * scale;
                        v = layer.Combine switch
                        {
                            HeightCombine.Maximum => Math.Max(v, below) + delta / w,
                            HeightCombine.Minimum => Math.Min(v, below) + delta / w,
                            _ => v + delta / w,
                        };
                        values[x, z] = Math.Clamp(v, -limit, limit);
                    }
                }
            }
            HeightLayer sculpted = layer with { Values = values.ToBuffer(), Coverage = coverage?.ToBuffer() };
            return document with { HeightLayers = document.HeightLayers.SetItem(target, sculpted) };
        }

        /// <summary>
        /// Whether two stacks differ only in their values: the same layers in the same order with the
        /// same settings, and the same heightmap scale.
        /// </summary>
        private static bool SameSettings(MapDocument previous, MapDocument next)
        {
            if (previous.HeightLayers.Length != next.HeightLayers.Length || previous.Terrain.Heightmap.Scale != next.Terrain.Heightmap.Scale)
            {
                return false;
            }
            for (int i = 0; i < next.HeightLayers.Length; i++)
            {
                HeightLayer a = previous.HeightLayers[i];
                HeightLayer b = next.HeightLayers[i];
                if (ReferenceEquals(a, b))
                {
                    continue;
                }
                if (a.Combine != b.Combine || a.Strength != b.Strength || a.Mask != b.Mask || a.Visible != b.Visible
                    || (a.Coverage is null) != (b.Coverage is null)
                    || a.Values.Width != b.Values.Width || a.Values.Height != b.Values.Height)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// A mask for tools renamed (the same pixels under a new name) stays the layers' mask.
        /// </summary>
        private static MapDocument FollowRenamedMasks(MapDocument previous, MapDocument next)
        {
            if (ReferenceEquals(previous.Masks, next.Masks))
            {
                return next;
            }
            ImmutableArray<HeightLayer> layers = next.HeightLayers;
            for (int i = 0; i < layers.Length; i++)
            {
                string? key = layers[i].Mask;
                if (key is null || !key.StartsWith(MaskPrefix, StringComparison.Ordinal) || MaskOf(next, key) is not null
                    || MaskOf(previous, key) is not TiledBuffer<byte> mask)
                {
                    continue;
                }
                if (next.Masks.FirstOrDefault(entry => ReferenceEquals(entry.Value, mask)).Key is string renamed)
                {
                    layers = layers.SetItem(i, layers[i] with { Mask = MaskKey(renamed) });
                }
            }
            return layers == next.HeightLayers ? next : next with { HeightLayers = layers };
        }

        private static void Mark(bool[] dirty, int tilesX, IEnumerable<(int X, int Y)> tiles)
        {
            foreach ((int x, int y) in tiles)
            {
                dirty[y * tilesX + x] = true;
            }
        }

        /// <summary>
        /// Marks the tiles of the heightmap whose corners read a part of a mask that changed.
        /// </summary>
        private static void MarkMask(bool[] dirty, TiledBuffer<ushort> samples, TiledBuffer<byte>? was, TiledBuffer<byte>? mask)
        {
            if (ReferenceEquals(was, mask))
            {
                return;
            }
            if (was is null || mask is null || was.Width != mask.Width || was.Height != mask.Height)
            {
                Array.Fill(dirty, true);
                return;
            }
            int mapWidth = samples.Width - 1;
            int mapHeight = samples.Height - 1;
            float perPixelX = mapWidth / (float)mask.Width;
            float perPixelZ = mapHeight / (float)mask.Height;
            foreach ((int x, int y) in mask.ChangedTiles(was))
            {
                // a pixel counts for the corners within one pixel of its centre
                int x0 = Math.Clamp((int)MathF.Floor((x * Size - 0.5f) * perPixelX), 0, mapWidth);
                int x1 = Math.Clamp((int)MathF.Ceiling(((x + 1) * Size + 0.5f) * perPixelX), 0, mapWidth);
                int z0 = Math.Clamp((int)MathF.Floor((y * Size - 0.5f) * perPixelZ), 0, mapHeight);
                int z1 = Math.Clamp((int)MathF.Ceiling(((y + 1) * Size + 0.5f) * perPixelZ), 0, mapHeight);
                for (int tileY = z0 / Size; tileY <= z1 / Size; tileY++)
                {
                    for (int tileX = x0 / Size; tileX <= x1 / Size; tileX++)
                    {
                        dirty[tileY * samples.TilesX + tileX] = true;
                    }
                }
            }
        }
    }
}
