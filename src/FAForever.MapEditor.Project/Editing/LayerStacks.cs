using System.Collections.Immutable;
using System.Globalization;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// The one compositor of layer stacks (<see cref="LayerStack"/>), for heights and for coverage:
    /// what a stack makes, tile by tile, and which tiles a change touches, so that a brush stroke on
    /// a large map adds up only the few tiles it reached.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per place the value starts at 0 and each visible layer, lowest first, works on it with its
    /// weight w (strength × clip mask × in a stack of heights its coverage) and its value v as its
    /// <see cref="LayerCombine"/> says. Heights are in game units; coverage runs from 0 to 1 (a byte
    /// over 255) and is kept within that after every layer. The base replaces at full weight, so a
    /// stack of only the base gives the base's values.
    /// </para>
    /// <para>
    /// A stack's grid is its target's: for the heightmap (width + 1) by (height + 1) corners, for
    /// coverage the target grid's own size (a mask of half the map's size stays half). Clip masks and
    /// mask references of any size cover the whole map and are read between their pixels
    /// (<see cref="SampleAt"/>).
    /// </para>
    /// <para>
    /// The changes of a target grid itself (a brush painting the heightmap or a stratum mask while
    /// it has a stack) go into the stack's chosen painted layer (<see cref="TakeChanges{T}"/>), so
    /// that the composite is what the brush made; <see cref="HeightLayers.Follow"/> does so for the
    /// heightmap after every step.
    /// </para>
    /// </remarks>
    public static class LayerStacks
    {
        /// <summary>
        /// How a layer names a mask for tools: <c>mask:rivers</c>.
        /// </summary>
        public const string MaskPrefix = "mask:";

        /// <summary>
        /// How a layer names the mask of a stratum: <c>stratum:3</c>.
        /// </summary>
        public const string StratumPrefix = "stratum:";

        private const int Size = TiledBuffer<float>.TileSize;

        /// <summary>
        /// The key of a mask for tools, for <see cref="Layer.Clip"/> and <see cref="MaskRefSource.Key"/>.
        /// </summary>
        public static string MaskKey(string name) => MaskPrefix + name;

        /// <summary>
        /// The key of the mask of stratum <paramref name="layer"/> (1 to 8).
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
        /// A mask's value at a corner of the map, 0 to 1, between the centres of its pixels.
        /// </summary>
        public static float MaskAt(TiledBuffer<byte> mask, int mapWidth, int mapHeight, int x, int z) => SampleAt(mask, mapWidth, mapHeight, x, z);

        /// <summary>
        /// A mask's value at a place of the map in ogrids, 0 to 1, between the centres of its pixels:
        /// a mask of any size (the map's, half of it, the stratum masks') covers the whole map.
        /// </summary>
        public static float SampleAt(TiledBuffer<byte> mask, int mapWidth, int mapHeight, float x, float z)
        {
            float u = x * mask.Width / Math.Max(1, mapWidth) - 0.5f;
            float v = z * mask.Height / Math.Max(1, mapHeight) - 0.5f;
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
        /// The keys of the masks a stack reads: its layers' clips and mask references.
        /// </summary>
        public static IEnumerable<string> MaskKeysOf(LayerStack stack) =>
            stack.Layers.Select(layer => layer.Clip)
                .Concat(stack.Layers.Select(layer => (layer.Source as MaskRefSource)?.Key))
                .OfType<string>()
                .Distinct(StringComparer.Ordinal);

        /// <summary>
        /// The grid of a stack in a document: its size and where its samples lie on the map.
        /// </summary>
        public static StackGrid GridOf(MapDocument document, LayerStack stack)
        {
            if (stack.Target.Grid is PaintTarget target)
            {
                TiledBuffer<byte> grid = target.Read(document);
                return new StackGrid(grid.Width, grid.Height, document.Width, document.Height, false);
            }
            return new StackGrid(document.Width + 1, document.Height + 1, document.Width, document.Height, true);
        }

        /// <summary>
        /// What a stack makes over the whole of its grid, in its units (game units for heights,
        /// 0 to 1 for coverage): for previews and for tools that read a stack that is not its
        /// target's (the heightmap's draft).
        /// </summary>
        public static TiledBuffer<float> Composite(MapDocument document, LayerStack stack)
        {
            StackGrid grid = GridOf(document, stack);
            Resolved[] layers = Resolve(document, stack);
            TiledBuffer<float>.Builder builder = TiledBuffer<float>.Create(grid.Width, grid.Height).ToBuilder();
            int tilesX = (grid.Width + Size - 1) / Size;
            int tilesY = (grid.Height + Size - 1) / Size;
            float[][] tiles = new float[tilesX * tilesY][];
            Parallel.For(0, tiles.Length, t =>
            {
                float[] values = new float[Size * Size];
                float[] weights = new float[Size * Size];
                Tile(layers, grid, t % tilesX, t / tilesX, values, weights);
                tiles[t] = values;
            });
            for (int t = 0; t < tiles.Length; t++)
            {
                builder.SetTile(t % tilesX, t / tilesX, tiles[t]);
            }
            return builder.ToBuffer();
        }

        /// <summary>
        /// A coverage stack's composite: <paramref name="current"/> (the target grid as it is) with
        /// the tiles marked in <paramref name="dirty"/> (all of them when null) made again; tiles that
        /// come out the same stay shared, so <paramref name="current"/> itself comes back when nothing
        /// changed.
        /// </summary>
        /// <param name="dirty">Per tile of the grid, row by row; see <see cref="ChangedTiles"/>.</param>
        public static TiledBuffer<byte> CompositeCoverage(MapDocument document, LayerStack stack, TiledBuffer<byte> current, bool[]? dirty = null)
        {
            if (stack.Target.IsHeights)
            {
                throw new ArgumentException($"The stack of {stack.Target} makes heights, not coverage", nameof(stack));
            }
            return CompositeInto(document, stack, GridOf(document, stack), current, new CoverageCodec(), dirty);
        }

        /// <summary>
        /// The heightmap's samples as a stack of heights makes them, the tiles marked in
        /// <paramref name="dirty"/> (all when null) made again; tiles that come out the same stay
        /// shared.
        /// </summary>
        public static TiledBuffer<ushort> CompositeHeights(MapDocument document, LayerStack stack, Heightmap current, bool[]? dirty = null)
        {
            if (!stack.Target.IsHeights)
            {
                throw new ArgumentException($"The stack of {stack.Target} makes coverage, not heights", nameof(stack));
            }
            return CompositeInto(document, stack, GridOf(document, stack), current.Samples, new HeightCodec(current.Scale), dirty);
        }

        /// <summary>
        /// The tiles of a stack's grid whose composite a change can have changed, per tile row by row:
        /// those whose layer values, coverage or masks read changed, or all of them when the stack's
        /// layers, their order or settings, or the grid's size changed.
        /// </summary>
        public static bool[] ChangedTiles(MapDocument previous, LayerStack before, MapDocument next, LayerStack after)
        {
            StackGrid grid = GridOf(next, after);
            bool[] dirty = new bool[grid.TilesX * grid.TilesY];
            if (GridOf(previous, before) != grid || !SameSettings(before, after))
            {
                Array.Fill(dirty, true);
                return dirty;
            }
            for (int i = 0; i < after.Layers.Length; i++)
            {
                Layer was = before.Layers[i];
                Layer layer = after.Layers[i];
                if (ReferenceEquals(was, layer))
                {
                    continue;
                }
                if (layer.Values is not null && was.Values is not null)
                {
                    Mark(dirty, grid.TilesX, layer.Values.ChangedTiles(was.Values));
                }
                if (layer.Coverage is not null && was.Coverage is not null)
                {
                    Mark(dirty, grid.TilesX, layer.Coverage.ChangedTiles(was.Coverage));
                }
            }
            if (!ReferenceEquals(previous.Masks, next.Masks) || !ReferenceEquals(previous.Terrain.StratumMasks, next.Terrain.StratumMasks))
            {
                foreach (string key in MaskKeysOf(after))
                {
                    MarkMask(dirty, grid, MaskOf(previous, key), MaskOf(next, key));
                }
            }
            return dirty;
        }

        /// <summary>
        /// The stack with a mask for tools that was renamed (the same pixels under a new name) still
        /// read by its layers' clips and mask references.
        /// </summary>
        public static LayerStack FollowRenamedMasks(MapDocument previous, MapDocument next, LayerStack stack)
        {
            if (ReferenceEquals(previous.Masks, next.Masks))
            {
                return stack;
            }
            string? Renamed(string? key)
            {
                if (key is null || !key.StartsWith(MaskPrefix, StringComparison.Ordinal) || MaskOf(next, key) is not null
                    || MaskOf(previous, key) is not TiledBuffer<byte> mask)
                {
                    return null;
                }
                return next.Masks.FirstOrDefault(entry => ReferenceEquals(entry.Value, mask)).Key is string name ? MaskKey(name) : null;
            }
            ImmutableArray<Layer> layers = stack.Layers;
            for (int i = 0; i < layers.Length; i++)
            {
                Layer layer = layers[i];
                if (Renamed(layer.Clip) is string clip)
                {
                    layer = layer with { Clip = clip };
                }
                if (layer.Source is MaskRefSource reference && Renamed(reference.Key) is string key)
                {
                    layer = layer with { Source = reference with { Key = key } };
                }
                layers = layers.SetItem(i, layer);
            }
            return layers.AsSpan().SequenceEqual(stack.Layers.AsSpan()) ? stack : stack with { Layers = layers };
        }

        /// <summary>
        /// Puts the changes of a target grid itself (tiles that are not what the stack makes) into
        /// the painted layer <paramref name="into"/>, so that the composite is what the brush made,
        /// and marks those tiles dirty. The brush works on the composite; the layer gets what makes
        /// the composite that (a weaker or clipped layer gets more; under a replacing layer it does
        /// not show). Without a layer that can be painted (locked, hidden, not painted) the stack
        /// stays as it is and the composite wins.
        /// </summary>
        /// <param name="before">The target grid as the stack made it before the change.</param>
        /// <param name="now">The target grid as the brush left it.</param>
        /// <param name="scale">Game units per step of a height sample; 1 for coverage.</param>
        /// <param name="dirty">Per tile; the tiles changed by hand are set.</param>
        /// <returns>The stack with the layer changed, or <paramref name="stack"/> itself.</returns>
        public static LayerStack TakeChanges<T>(MapDocument document, LayerStack stack, TiledBuffer<T> before, TiledBuffer<T> now, float scale, string? into, bool[] dirty)
            where T : unmanaged
        {
            if (typeof(T) == typeof(ushort))
            {
                return TakeChanges(document, stack, (TiledBuffer<ushort>)(object)before, (TiledBuffer<ushort>)(object)now, new HeightCodec(scale), into, dirty);
            }
            if (typeof(T) == typeof(byte))
            {
                return TakeChanges(document, stack, (TiledBuffer<byte>)(object)before, (TiledBuffer<byte>)(object)now, new CoverageCodec(), into, dirty);
            }
            throw new ArgumentException($"A target grid holds height samples or coverage bytes, not {typeof(T).Name}");
        }

        /// <summary>
        /// Why a brush cannot paint into a layer of a stack, or null when it can.
        /// </summary>
        /// <param name="what">What a layer is called in the messages: <c>height layer</c>.</param>
        /// <param name="verb">What the brush does in the messages: <c>sculpt</c>.</param>
        public static string? PaintProblem(LayerStack stack, string? name, string what = "layer", string verb = "paint")
        {
            if (stack.Find(name) is not Layer layer)
            {
                return $"Choose a {what} to {verb} into";
            }
            if (layer.Locked)
            {
                return $"The {what} {layer.Name} is locked";
            }
            if (!layer.Visible)
            {
                return $"The {what} {layer.Name} is hidden: show it to {verb} into it";
            }
            return layer.Source switch
            {
                PaintedSource => null,
                CurveSource => $"The {what} {layer.Name} follows its curve: edit the curve, or {verb} into another layer",
                ImageSource image => $"The {what} {layer.Name} is read from {image.Path}: {verb} into a layer above it",
                MaskRefSource reference => $"The {what} {layer.Name} reads {MaskTitle(reference.Key)}: {verb} into another layer",
                RulesSource => $"The {what} {layer.Name} is made by its rules: {verb} into another layer",
                DecalMasksSource => $"The {what} {layer.Name} follows the decals' masks: move the decals, or {verb} into another layer",
                _ => $"The {what} {layer.Name} cannot be {verb}ed",
            };
        }

        /// <summary>
        /// What the layers below <paramref name="upTo"/> (all when it is the count) make over one
        /// tile, in the stack's units.
        /// </summary>
        public static void TileOf(MapDocument document, LayerStack stack, int tileX, int tileY, int upTo, Span<float> values)
        {
            Resolved[] layers = Resolve(document, stack);
            float[] weights = new float[Size * Size];
            Tile(layers.AsSpan(0, upTo), GridOf(document, stack), tileX, tileY, values, weights);
        }

        /// <summary>
        /// Whether two tiles of a buffer's size hold the same values inside the buffer (the part of an
        /// edge tile outside it does not count).
        /// </summary>
        internal static bool SameTile<TSize, T>(TiledBuffer<TSize> size, int tileX, int tileY, ReadOnlySpan<T> a, ReadOnlySpan<T> b)
            where TSize : unmanaged where T : unmanaged, IEquatable<T>
        {
            int columns = Math.Min(Size, size.Width - tileX * Size);
            int rows = Math.Min(Size, size.Height - tileY * Size);
            for (int row = 0; row < rows; row++)
            {
                if (!a.Slice(row * Size, columns).SequenceEqual(b.Slice(row * Size, columns)))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// A layer and the masks it reads, looked up once for compositing many tiles.
        /// </summary>
        internal readonly record struct Resolved(Layer Layer, TiledBuffer<byte>? Clip, TiledBuffer<byte>? Reference);

        internal static Resolved[] Resolve(MapDocument document, LayerStack stack) =>
            [.. stack.Layers.Select(layer => new Resolved(layer, MaskOf(document, layer.Clip), MaskOf(document, (layer.Source as MaskRefSource)?.Key)))];

        /// <summary>
        /// What the layers make over one tile of the grid.
        /// </summary>
        internal static void Tile(ReadOnlySpan<Resolved> layers, StackGrid grid, int tileX, int tileY, Span<float> values, Span<float> weights)
        {
            values.Clear();
            int columns = Math.Min(Size, grid.Width - tileX * Size);
            int rows = Math.Min(Size, grid.Height - tileY * Size);
            bool heights = grid.Corners;
            foreach (Resolved resolved in layers)
            {
                Layer layer = resolved.Layer;
                if (!layer.Visible || layer.Strength <= 0)
                {
                    continue;
                }
                ReadOnlySpan<float> heightValues = default;
                ReadOnlySpan<byte> coverageValues = default;
                MaskRefSource? reference = layer.Source as MaskRefSource;
                if (reference is not null)
                {
                    if (resolved.Reference is null)
                    {
                        continue;
                    }
                }
                else if (heights)
                {
                    if (layer.Values is null)
                    {
                        continue;
                    }
                    heightValues = layer.Values.Tile(tileX, tileY);
                }
                else
                {
                    if (layer.Coverage is null)
                    {
                        continue;
                    }
                    coverageValues = layer.Coverage.Tile(tileX, tileY);
                }

                Weights(resolved, grid, tileX, tileY, columns, rows, weights);
                if (heights && reference is null)
                {
                    // the common case, kept free of the others' branches: a stroke composites it for every dab
                    LayerCombine combine = layer.Combine;
                    for (int row = 0; row < rows; row++)
                    {
                        int start = row * Size;
                        for (int i = start; i < start + columns; i++)
                        {
                            float w = weights[i];
                            if (w > 0)
                            {
                                values[i] = Combine(combine, values[i], heightValues[i], w);
                            }
                        }
                    }
                    continue;
                }
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
                        float v;
                        if (reference is not null)
                        {
                            float m = SampleAt(resolved.Reference!, grid.MapWidth, grid.MapHeight, grid.X(tileX * Size + column), grid.Z(tileY * Size + row));
                            m = reference.Invert ? 1 - m : m;
                            m = m < reference.Threshold ? 0 : m;
                            v = heights ? m * reference.Height : m;
                        }
                        else
                        {
                            v = heights ? heightValues[i] : coverageValues[i] / 255f;
                        }
                        float h = values[i];
                        h = Combine(layer.Combine, h, v, w);
                        values[i] = heights ? h : Math.Clamp(h, 0, 1);
                    }
                }
            }
        }

        /// <summary>
        /// One layer's work at a place: h what the layers below made, v its value, w its weight.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal static float Combine(LayerCombine combine, float h, float v, float w) => combine switch
        {
            LayerCombine.Add => h + w * v,
            LayerCombine.Subtract => h - w * v,
            LayerCombine.Maximum => v > h ? h + w * (v - h) : h,
            LayerCombine.Minimum => v < h ? h + w * (v - h) : h,
            LayerCombine.Multiply => h + w * (h * v - h),
            _ => h + w * (v - h),
        };

        /// <summary>
        /// A layer's weight at each place of a tile: strength × clip × (in a stack of heights) coverage.
        /// </summary>
        private static void Weights(Resolved resolved, StackGrid grid, int tileX, int tileY, int columns, int rows, Span<float> weights)
        {
            Layer layer = resolved.Layer;
            weights.Fill(Math.Clamp(layer.Strength, 0, 1));
            if (grid.Corners && layer.Coverage is TiledBuffer<byte> coverage && layer.Source is not MaskRefSource)
            {
                ReadOnlySpan<byte> covered = coverage.Tile(tileX, tileY);
                for (int i = 0; i < weights.Length; i++)
                {
                    weights[i] *= covered[i] / 255f;
                }
            }
            if (resolved.Clip is TiledBuffer<byte> clip)
            {
                for (int row = 0; row < rows; row++)
                {
                    float z = grid.Z(tileY * Size + row);
                    for (int column = 0; column < columns; column++)
                    {
                        int i = row * Size + column;
                        if (weights[i] > 0)
                        {
                            weights[i] *= SampleAt(clip, grid.MapWidth, grid.MapHeight, grid.X(tileX * Size + column), z);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Turns composite values into a target grid's samples and back.
        /// </summary>
        private interface ICodec<T> where T : unmanaged
        {
            /// <summary>
            /// The composite units of one step of a sample.
            /// </summary>
            float Step { get; }

            T Encode(float value);

            float Decode(T sample);
        }

        private readonly struct HeightCodec(float scale) : ICodec<ushort>
        {
            public float Step => scale;

            public ushort Encode(float value) => (ushort)Math.Clamp(MathF.Round(value / scale), 0, ushort.MaxValue);

            public float Decode(ushort sample) => sample * scale;
        }

        private readonly struct CoverageCodec : ICodec<byte>
        {
            public float Step => 1 / 255f;

            public byte Encode(float value) => (byte)Math.Clamp(MathF.Round(value * 255), 0, 255);

            public float Decode(byte sample) => sample / 255f;
        }

        private static TiledBuffer<T> CompositeInto<T, TCodec>(MapDocument document, LayerStack stack, StackGrid grid, TiledBuffer<T> current, TCodec codec, bool[]? dirty)
            where T : unmanaged, IEquatable<T> where TCodec : struct, ICodec<T>
        {
            if (current.Width != grid.Width || current.Height != grid.Height)
            {
                throw new ArgumentException($"The stack of {stack.Target} makes {grid.Width} by {grid.Height} values, the grid has {current.Width} by {current.Height}");
            }
            int tilesX = current.TilesX;
            int count = tilesX * current.TilesY;
            Resolved[] layers = Resolve(document, stack);
            T[]?[] results = new T[count][];
            Parallel.For(0, count, () => (Values: new float[Size * Size], Weights: new float[Size * Size]), (t, _, buffers) =>
            {
                if (dirty is not null && !dirty[t])
                {
                    return buffers;
                }
                int tileX = t % tilesX;
                int tileY = t / tilesX;
                Tile(layers, grid, tileX, tileY, buffers.Values, buffers.Weights);
                ReadOnlySpan<T> held = current.Tile(tileX, tileY);
                int columns = Math.Min(Size, grid.Width - tileX * Size);
                int rows = Math.Min(Size, grid.Height - tileY * Size);
                T[]? tile = null;
                for (int row = 0; row < rows; row++)
                {
                    for (int column = 0; column < columns; column++)
                    {
                        int i = row * Size + column;
                        T sample = codec.Encode(buffers.Values[i]);
                        if (!sample.Equals(held[i]))
                        {
                            tile ??= held.ToArray();
                            tile[i] = sample;
                        }
                    }
                }
                results[t] = tile;
                return buffers;
            }, _ => { });

            TiledBuffer<T>.Builder? builder = null;
            for (int t = 0; t < results.Length; t++)
            {
                if (results[t] is T[] tile)
                {
                    builder ??= current.ToBuilder();
                    builder.SetTile(t % tilesX, t / tilesX, tile);
                }
            }
            return builder is null ? current : builder.ToBuffer();
        }

        private static LayerStack TakeChanges<T, TCodec>(MapDocument document, LayerStack stack, TiledBuffer<T> before, TiledBuffer<T> now, TCodec codec, string? into, bool[] dirty)
            where T : unmanaged, IEquatable<T> where TCodec : struct, ICodec<T>
        {
            StackGrid grid = GridOf(document, stack);
            Resolved[] layers = Resolve(document, stack);
            float[] values = new float[Size * Size];
            float[] weights = new float[Size * Size];
            List<(int X, int Y)> changed = [];
            foreach ((int tileX, int tileY) in now.ChangedTiles(before))
            {
                Tile(layers, grid, tileX, tileY, values, weights);
                ReadOnlySpan<T> held = now.Tile(tileX, tileY);
                int columns = Math.Min(Size, grid.Width - tileX * Size);
                int rows = Math.Min(Size, grid.Height - tileY * Size);
                bool composite = true;
                for (int row = 0; row < rows && composite; row++)
                {
                    for (int column = 0; column < columns; column++)
                    {
                        int i = row * Size + column;
                        if (!codec.Encode(values[i]).Equals(held[i]))
                        {
                            composite = false;
                            break;
                        }
                    }
                }
                if (!composite)
                {
                    changed.Add((tileX, tileY));
                    dirty[tileY * grid.TilesX + tileX] = true;
                }
            }
            int target = PaintProblem(stack, into) is null ? stack.IndexOf(into) : -1;
            if (changed.Count == 0 || target < 0)
            {
                return stack;
            }

            Layer layer = stack.Layers[target];
            bool heights = grid.Corners;
            TiledBuffer<float>.Builder? heightValues = heights ? layer.Values?.ToBuilder() : null;
            TiledBuffer<byte>.Builder? coverage = layer.Coverage?.ToBuilder();
            if ((heights && heightValues is null) || (!heights && coverage is null))
            {
                return stack;
            }
            float limit = heights ? ushort.MaxValue * codec.Step * 2 : 1;
            TiledBuffer<byte>? clip = MaskOf(document, layer.Clip);
            float strength = Math.Clamp(layer.Strength, 0, 1);
            foreach ((int tileX, int tileY) in changed)
            {
                Tile(layers.AsSpan(0, target), grid, tileX, tileY, values, weights);
                ReadOnlySpan<T> after = now.Tile(tileX, tileY);
                ReadOnlySpan<T> was = before.Tile(tileX, tileY);
                int columns = Math.Min(Size, grid.Width - tileX * Size);
                int rows = Math.Min(Size, grid.Height - tileY * Size);
                for (int row = 0; row < rows; row++)
                {
                    for (int column = 0; column < columns; column++)
                    {
                        int i = row * Size + column;
                        if (after[i].Equals(was[i]))
                        {
                            continue;
                        }
                        int x = tileX * Size + column;
                        int z = tileY * Size + row;
                        float w = strength * (clip is null ? 1 : SampleAt(clip, grid.MapWidth, grid.MapHeight, grid.X(x), grid.Z(z)));
                        float below = values[i];
                        float v = heights ? heightValues![x, z] : coverage![x, z] / 255f;
                        if (heights && coverage is not null)
                        {
                            byte covered = coverage[x, z];
                            if (covered == 0 && w > 0)
                            {
                                // a part of a replacing layer not sculpted before starts from the height below
                                coverage[x, z] = 255;
                                v = layer.Combine == LayerCombine.Add ? 0 : below;
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
                        float delta = codec.Decode(after[i]) - codec.Decode(was[i]);
                        v = layer.Combine switch
                        {
                            LayerCombine.Maximum => Math.Max(v, below) + delta / w,
                            LayerCombine.Minimum => Math.Min(v, below) + delta / w,
                            LayerCombine.Subtract => v - delta / w,
                            LayerCombine.Multiply => Math.Abs(below) < 1e-6f ? v : v + delta / (w * below),
                            _ => v + delta / w,
                        };
                        if (heights)
                        {
                            heightValues![x, z] = Math.Clamp(v, -limit, limit);
                        }
                        else
                        {
                            coverage![x, z] = (byte)Math.Clamp(MathF.Round(v * 255), 0, 255);
                        }
                    }
                }
            }
            Layer painted = layer with { Values = heightValues?.ToBuffer() ?? layer.Values, Coverage = coverage?.ToBuffer() };
            return stack.With(target, painted);
        }

        /// <summary>
        /// Whether two stacks differ only in their layers' grids: the same layers in the same order
        /// with the same settings and kinds of source.
        /// </summary>
        private static bool SameSettings(LayerStack before, LayerStack after)
        {
            if (before.Layers.Length != after.Layers.Length || before.Target != after.Target)
            {
                return false;
            }
            for (int i = 0; i < after.Layers.Length; i++)
            {
                Layer a = before.Layers[i];
                Layer b = after.Layers[i];
                if (ReferenceEquals(a, b))
                {
                    continue;
                }
                if (a.Combine != b.Combine || a.Strength != b.Strength || a.Clip != b.Clip || a.Visible != b.Visible || a.IsBase != b.IsBase
                    || a.Source.GetType() != b.Source.GetType() || (a.Source is MaskRefSource && a.Source != b.Source)
                    || (a.Values is null) != (b.Values is null) || (a.Coverage is null) != (b.Coverage is null)
                    || (a.Values is not null && (a.Values.Width != b.Values!.Width || a.Values.Height != b.Values.Height))
                    || (a.Coverage is not null && (a.Coverage.Width != b.Coverage!.Width || a.Coverage.Height != b.Coverage.Height)))
                {
                    return false;
                }
            }
            return true;
        }

        private static void Mark(bool[] dirty, int tilesX, IEnumerable<(int X, int Y)> tiles)
        {
            foreach ((int x, int y) in tiles)
            {
                int index = y * tilesX + x;
                if (index < dirty.Length)
                {
                    dirty[index] = true;
                }
            }
        }

        /// <summary>
        /// Marks the tiles of a stack's grid whose samples read a part of a mask that changed.
        /// </summary>
        private static void MarkMask(bool[] dirty, StackGrid grid, TiledBuffer<byte>? was, TiledBuffer<byte>? mask)
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
            float perPixelX = grid.MapWidth / (float)mask.Width;
            float perPixelZ = grid.MapHeight / (float)mask.Height;
            foreach ((int x, int y) in mask.ChangedTiles(was))
            {
                // a pixel counts for the samples within one pixel of its centre
                (int x0, int x1) = grid.ColumnsOf((x * Size - 0.5f) * perPixelX, ((x + 1) * Size + 0.5f) * perPixelX);
                (int z0, int z1) = grid.RowsOf((y * Size - 0.5f) * perPixelZ, ((y + 1) * Size + 0.5f) * perPixelZ);
                for (int tileY = z0 / Size; tileY <= z1 / Size; tileY++)
                {
                    for (int tileX = x0 / Size; tileX <= x1 / Size; tileX++)
                    {
                        dirty[tileY * grid.TilesX + tileX] = true;
                    }
                }
            }
        }
    }

    /// <summary>
    /// The grid of a layer stack: its size, the map's, and where its samples lie (at the corners of
    /// the ogrids for heights, at the centres of its pixels for coverage).
    /// </summary>
    public readonly record struct StackGrid(int Width, int Height, int MapWidth, int MapHeight, bool Corners)
    {
        public int TilesX => (Width + TiledBuffer<float>.TileSize - 1) / TiledBuffer<float>.TileSize;

        public int TilesY => (Height + TiledBuffer<float>.TileSize - 1) / TiledBuffer<float>.TileSize;

        /// <summary>
        /// The place on the map, in ogrids, of column <paramref name="column"/>.
        /// </summary>
        public float X(int column) => Corners ? column : (column + 0.5f) * MapWidth / Width;

        /// <summary>
        /// The place on the map, in ogrids, of row <paramref name="row"/>.
        /// </summary>
        public float Z(int row) => Corners ? row : (row + 0.5f) * MapHeight / Height;

        /// <summary>
        /// The columns whose places lie from <paramref name="from"/> to <paramref name="to"/> ogrids.
        /// </summary>
        public (int From, int To) ColumnsOf(float from, float to) => Range(from, to, Width, MapWidth);

        public (int From, int To) RowsOf(float from, float to) => Range(from, to, Height, MapHeight);

        private (int From, int To) Range(float from, float to, int size, int map)
        {
            if (Corners)
            {
                return (Math.Clamp((int)MathF.Floor(from), 0, size - 1), Math.Clamp((int)MathF.Ceiling(to), 0, size - 1));
            }
            float perOgrid = size / (float)Math.Max(1, map);
            return (Math.Clamp((int)MathF.Floor(from * perOgrid - 0.5f), 0, size - 1), Math.Clamp((int)MathF.Ceiling(to * perOgrid - 0.5f), 0, size - 1));
        }
    }
}
