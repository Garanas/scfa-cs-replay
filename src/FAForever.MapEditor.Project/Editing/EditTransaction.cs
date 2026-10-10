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
    /// (<see cref="Heightmap"/>, <see cref="StratumMask"/>, <see cref="Grid"/>), which copy a tile only the first time
    /// they change it; the builders' changes are in <see cref="Document"/> from the next read of it.
    /// </remarks>
    public sealed class EditTransaction : IDisposable
    {
        private readonly EditSession _session;
        private MapDocument _document;
        private MapDocument _followed;
        private TiledBuffer<ushort>.Builder? _heightmap;
        private readonly Dictionary<PaintTarget, TiledBuffer<byte>.Builder> _grids = [];
        private bool _ended;

        internal EditTransaction(EditSession session, MapDocument document, string description)
        {
            _session = session;
            _document = document;
            _followed = document;
            Description = description;
        }

        /// <summary>
        /// The step's name in the history; the code that edits may name it after what it did
        /// before the transaction ends (<c>Move 3 units into INITIAL of ARMY_1</c>).
        /// </summary>
        public string Description { get; set; }

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
        /// The heights, to change sample by sample. While the map has height layers (user story T-5),
        /// what is written goes into the session's sculpting layer (<see cref="EditSession.SculptLayer"/>)
        /// at the next read of <see cref="Document"/>, and the builder then starts again from the
        /// heights the layers add up to.
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
            return Grid(PaintTarget.Stratum(layer));
        }

        /// <summary>
        /// A grid of the document (a mask, the terrain's types), to change pixel by pixel.
        /// </summary>
        /// <exception cref="ArgumentException">The document has no such grid.</exception>
        public TiledBuffer<byte>.Builder Grid(PaintTarget target)
        {
            EnsureOpen();
            if (!_grids.TryGetValue(target, out TiledBuffer<byte>.Builder? builder))
            {
                builder = target.Read(_document).ToBuilder();
                _grids[target] = builder;
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
            _grids.Clear();
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
            foreach ((PaintTarget target, TiledBuffer<byte>.Builder builder) in _grids)
            {
                TiledBuffer<byte> grid = builder.ToBuffer();
                if (!ReferenceEquals(grid, target.Read(_document)))
                {
                    _document = target.Write(_document, grid);
                }
            }

            // the heightmap stays what the height layers add up to, a brush's work in the sculpting layer
            if (!ReferenceEquals(_followed, _document) && !_document.HeightLayers.IsEmpty)
            {
                MapDocument followed = HeightLayers.Follow(_followed, _document, _session.SculptLayer);
                if (!ReferenceEquals(followed.Terrain.Heightmap.Samples, _document.Terrain.Heightmap.Samples))
                {
                    _heightmap = null;
                }
                _document = followed;
            }
            _followed = _document;
        }

        private void EnsureOpen()
        {
            ObjectDisposedException.ThrowIf(_ended, this);
        }
    }
}
