namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// An immutable grid of values (heights, mask values, pixels), stored in square tiles that the
    /// versions of a buffer share. Changing a value through a <see cref="Builder"/> copies only its
    /// tile, so a brush stroke on a 2049 by 2049 heightmap costs the few tiles it touches, and every
    /// earlier version stays as it was (which is what undo needs).
    /// </summary>
    /// <remarks>
    /// Two buffers that share a tile hold the same values in it; <see cref="ChangedTiles"/> lists the
    /// tiles that differ by reference, which is what a renderer redraws.
    /// </remarks>
    public sealed class TiledBuffer<T> where T : unmanaged
    {
        /// <summary>
        /// The width and height of a tile; the tiles at the right and bottom edges may stick out of
        /// the buffer.
        /// </summary>
        public const int TileSize = 64;

        private readonly T[][] _tiles;

        private TiledBuffer(int width, int height, T[][] tiles)
        {
            Width = width;
            Height = height;
            _tiles = tiles;
        }

        public int Width { get; }

        public int Height { get; }

        /// <summary>
        /// The number of tiles across.
        /// </summary>
        public int TilesX => (Width + TileSize - 1) / TileSize;

        /// <summary>
        /// The number of tiles down.
        /// </summary>
        public int TilesY => (Height + TileSize - 1) / TileSize;

        public T this[int x, int y]
        {
            get
            {
                CheckBounds(x, y);
                return _tiles[TileIndex(x, y)][CellIndex(x, y)];
            }
        }

        /// <summary>
        /// A buffer with every value <paramref name="fill"/>. All its tiles are one shared tile until
        /// they are written to, so a large empty buffer costs one tile.
        /// </summary>
        public static TiledBuffer<T> Create(int width, int height, T fill = default)
        {
            CheckSize(width, height);
            T[] tile = new T[TileSize * TileSize];
            Array.Fill(tile, fill);
            T[][] tiles = new T[TileCount(width, height)][];
            Array.Fill(tiles, tile);
            return new TiledBuffer<T>(width, height, tiles);
        }

        /// <summary>
        /// A buffer of the values of <paramref name="values"/>, row by row.
        /// </summary>
        public static TiledBuffer<T> FromArray(int width, int height, ReadOnlySpan<T> values)
        {
            CheckSize(width, height);
            if (values.Length != width * height)
            {
                throw new ArgumentException($"{values.Length} values do not fill {width} by {height}", nameof(values));
            }

            int tilesX = (width + TileSize - 1) / TileSize;
            T[][] tiles = new T[TileCount(width, height)][];
            for (int t = 0; t < tiles.Length; t++)
            {
                T[] tile = new T[TileSize * TileSize];
                int x0 = t % tilesX * TileSize;
                int y0 = t / tilesX * TileSize;
                int columns = Math.Min(TileSize, width - x0);
                for (int row = 0; row < TileSize && y0 + row < height; row++)
                {
                    values.Slice((y0 + row) * width + x0, columns).CopyTo(tile.AsSpan(row * TileSize, columns));
                }
                tiles[t] = tile;
            }
            return new TiledBuffer<T>(width, height, tiles);
        }

        /// <summary>
        /// The values row by row.
        /// </summary>
        public T[] ToArray()
        {
            T[] values = new T[Width * Height];
            for (int t = 0; t < _tiles.Length; t++)
            {
                int x0 = t % TilesX * TileSize;
                int y0 = t / TilesX * TileSize;
                int columns = Math.Min(TileSize, Width - x0);
                for (int row = 0; row < TileSize && y0 + row < Height; row++)
                {
                    _tiles[t].AsSpan(row * TileSize, columns).CopyTo(values.AsSpan((y0 + row) * Width + x0, columns));
                }
            }
            return values;
        }

        /// <summary>
        /// The tiles (column, row) whose values may differ from <paramref name="other"/>'s: those it
        /// does not share. Buffers of another size differ everywhere.
        /// </summary>
        public IEnumerable<(int X, int Y)> ChangedTiles(TiledBuffer<T> other)
        {
            bool sameSize = other.Width == Width && other.Height == Height;
            for (int t = 0; t < _tiles.Length; t++)
            {
                if (!sameSize || !ReferenceEquals(_tiles[t], other._tiles[t]))
                {
                    yield return (t % TilesX, t / TilesX);
                }
            }
        }

        /// <summary>
        /// A builder that starts from this buffer's values.
        /// </summary>
        public Builder ToBuilder() => new Builder(this);

        /// <summary>
        /// Changes the values of a buffer, copying a tile the first time a value in it changes. The
        /// buffers it makes (<see cref="ToBuffer"/>) never change afterwards.
        /// </summary>
        public sealed class Builder
        {
            private readonly T[][] _tiles;
            private readonly bool[] _owned;
            private TiledBuffer<T> _last;
            private bool _changed;

            internal Builder(TiledBuffer<T> source)
            {
                Width = source.Width;
                Height = source.Height;
                _tiles = (T[][])source._tiles.Clone();
                _owned = new bool[_tiles.Length];
                _last = source;
            }

            public int Width { get; }

            public int Height { get; }

            public T this[int x, int y]
            {
                get
                {
                    CheckBounds(x, y);
                    return _tiles[TileIndex(x, y)][CellIndex(x, y)];
                }
                set
                {
                    CheckBounds(x, y);
                    int tile = TileIndex(x, y);
                    if (!_owned[tile])
                    {
                        _tiles[tile] = (T[])_tiles[tile].Clone();
                        _owned[tile] = true;
                    }
                    _tiles[tile][CellIndex(x, y)] = value;
                    _changed = true;
                }
            }

            /// <summary>
            /// The buffer as it is now: the same buffer as before when nothing changed since. Later
            /// changes copy their tiles again, so this buffer keeps its values.
            /// </summary>
            public TiledBuffer<T> ToBuffer()
            {
                if (_changed)
                {
                    Array.Clear(_owned);
                    _last = new TiledBuffer<T>(Width, Height, (T[][])_tiles.Clone());
                    _changed = false;
                }
                return _last;
            }

            private int TileIndex(int x, int y) => y / TileSize * ((Width + TileSize - 1) / TileSize) + x / TileSize;

            private void CheckBounds(int x, int y)
            {
                if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
                {
                    throw new ArgumentOutOfRangeException(null, $"({x}, {y}) lies outside {Width} by {Height}");
                }
            }
        }

        private int TileIndex(int x, int y) => y / TileSize * TilesX + x / TileSize;

        private static int CellIndex(int x, int y) => y % TileSize * TileSize + x % TileSize;

        private void CheckBounds(int x, int y)
        {
            if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
            {
                throw new ArgumentOutOfRangeException(null, $"({x}, {y}) lies outside {Width} by {Height}");
            }
        }

        private static int TileCount(int width, int height) =>
            (width + TileSize - 1) / TileSize * ((height + TileSize - 1) / TileSize);

        private static void CheckSize(int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), $"A buffer of {width} by {height} holds nothing");
            }
        }
    }
}
