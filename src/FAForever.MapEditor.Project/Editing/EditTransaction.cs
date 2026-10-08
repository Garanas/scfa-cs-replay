using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// Changes that become one step of an <see cref="EditSession"/>'s history when committed, e.g. a
    /// brush stroke from mouse down to mouse up, or a plugin run. Disposing it without
    /// <see cref="Commit"/> throws the changes away.
    /// </summary>
    /// <remarks>
    /// Edits of records go through <see cref="Apply"/>. Brushes write the grids through builders
    /// (<see cref="Heightmap"/>, <see cref="StratumMask"/>), which copy a tile only the first time
    /// they change it; the builders' changes are in <see cref="Document"/> from the next read of it.
    /// </remarks>
    public sealed class EditTransaction : IDisposable
    {
        private readonly EditSession _session;
        private MapDocument _document;
        private TiledBuffer<ushort>.Builder? _heightmap;
        private readonly Dictionary<int, TiledBuffer<byte>.Builder> _masks = [];
        private bool _ended;

        internal EditTransaction(EditSession session, MapDocument document, string description)
        {
            _session = session;
            _document = document;
            Description = description;
        }

        public string Description { get; }

        /// <summary>
        /// The document with the changes made so far.
        /// </summary>
        public MapDocument Document
        {
            get
            {
                EnsureOpen();
                Flush();
                return _document;
            }
        }

        /// <summary>
        /// The heights, to change sample by sample.
        /// </summary>
        public TiledBuffer<ushort>.Builder Heightmap
        {
            get
            {
                EnsureOpen();
                return _heightmap ??= _document.Terrain.Heightmap.Samples.ToBuilder();
            }
        }

        /// <summary>
        /// The mask of terrain layer <paramref name="layer"/> (1 to 8, or 1 to 4 in version 53), to
        /// change pixel by pixel.
        /// </summary>
        public TiledBuffer<byte>.Builder StratumMask(int layer)
        {
            EnsureOpen();
            if (layer < 1 || layer > _document.Terrain.StratumMasks.Layers.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(layer), $"Layer {layer} has no mask; layers 1 to {_document.Terrain.StratumMasks.Layers.Length} do");
            }
            if (!_masks.TryGetValue(layer, out TiledBuffer<byte>.Builder? builder))
            {
                builder = _document.Terrain.StratumMasks.Layers[layer - 1].ToBuilder();
                _masks[layer] = builder;
            }
            return builder;
        }

        /// <summary>
        /// Changes the document. The builders handed out before see the result: they start again
        /// from it when they are asked for next.
        /// </summary>
        public void Apply(Func<MapDocument, MapDocument> edit)
        {
            EnsureOpen();
            Flush();
            _document = edit(_document);
            _heightmap = null;
            _masks.Clear();
        }

        /// <summary>
        /// Keeps the changes as one step of the session's history.
        /// </summary>
        /// <returns>Whether the document changed.</returns>
        public bool Commit()
        {
            EnsureOpen();
            Flush();
            _ended = true;
            return _session.End(this, _document);
        }

        /// <summary>
        /// Throws the changes away unless they were committed.
        /// </summary>
        public void Dispose()
        {
            if (!_ended)
            {
                _ended = true;
                _session.End(this, null);
            }
        }

        /// <summary>
        /// Puts what the builders changed into the document.
        /// </summary>
        private void Flush()
        {
            if (_heightmap?.ToBuffer() is TiledBuffer<ushort> heights && !ReferenceEquals(heights, _document.Terrain.Heightmap.Samples))
            {
                Terrain terrain = _document.Terrain;
                _document = _document with { Terrain = terrain with { Heightmap = terrain.Heightmap with { Samples = heights } } };
            }
            foreach ((int layer, TiledBuffer<byte>.Builder builder) in _masks)
            {
                TiledBuffer<byte> mask = builder.ToBuffer();
                StratumMasks masks = _document.Terrain.StratumMasks;
                if (!ReferenceEquals(mask, masks.Layers[layer - 1]))
                {
                    masks = masks with { Layers = masks.Layers.SetItem(layer - 1, mask) };
                    _document = _document with { Terrain = _document.Terrain with { StratumMasks = masks } };
                }
            }
        }

        private void EnsureOpen()
        {
            ObjectDisposedException.ThrowIf(_ended, this);
        }
    }
}
