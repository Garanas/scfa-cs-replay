using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// Why the document of an <see cref="EditSession"/> changed.
    /// </summary>
    public enum ChangeReason
    {
        Edit,
        Undo,
        Redo,
    }

    /// <summary>
    /// The document before and after a change. Parts that did not change are the same objects in
    /// both, so a view finds what to redraw by comparing references, and the tiles of a grid with
    /// <see cref="TiledBuffer{T}.ChangedTiles"/>.
    /// </summary>
    /// <param name="Description">The edit that was made, undone or redone, e.g. <c>Raise terrain</c>.</param>
    public sealed record DocumentChange(MapDocument Previous, MapDocument Current, string Description, ChangeReason Reason);

    /// <summary>
    /// The one way a document changes while it is edited: every change, from the user interface or
    /// a plugin, is an edit that can be undone and redone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The session keeps the versions of the document. An edit is a function from one version to
    /// the next (<see cref="Edit"/>), or a transaction that collects many changes into one step
    /// (<see cref="Begin"/>), such as a brush stroke or a plugin run. Undo and redo move between the
    /// versions; since versions share what they do not change, each costs only what it changed.
    /// </para>
    /// <para>
    /// A session is not thread safe: use it from one thread, the user interface's.
    /// </para>
    /// </remarks>
    public sealed class EditSession
    {
        private readonly List<(MapDocument Document, string Description)> _versions;
        private int _current;
        private EditTransaction? _open;

        /// <param name="historyLimit">How many edits can be undone; older ones are forgotten.</param>
        public EditSession(MapDocument document, int historyLimit = 200)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(historyLimit);
            _versions = [(document, "")];
            HistoryLimit = historyLimit;
        }

        public MapDocument Document => _versions[_current].Document;

        public int HistoryLimit { get; }

        public bool CanUndo => _current > 0;

        public bool CanRedo => _current < _versions.Count - 1;

        /// <summary>
        /// The edit that <see cref="Undo"/> would undo, e.g. for a menu item <c>Undo Raise terrain</c>.
        /// </summary>
        public string? UndoDescription => CanUndo ? _versions[_current].Description : null;

        /// <summary>
        /// The edit that <see cref="Redo"/> would redo.
        /// </summary>
        public string? RedoDescription => CanRedo ? _versions[_current + 1].Description : null;

        /// <summary>
        /// Whether a transaction is open; no other change can be made until it ends.
        /// </summary>
        public bool InTransaction => _open is not null;

        /// <summary>
        /// Raised after every edit, undo and redo.
        /// </summary>
        public event EventHandler<DocumentChange>? Changed;

        /// <summary>
        /// Makes one edit, which can be undone as one step.
        /// </summary>
        /// <returns>Whether the document changed; an edit that changes nothing is not kept.</returns>
        public bool Edit(string description, Func<MapDocument, MapDocument> edit)
        {
            EnsureNoTransaction();
            return Record(description, edit(Document));
        }

        /// <summary>
        /// Starts a transaction: changes that are undone as one step, and that are thrown away
        /// unless the transaction is committed.
        /// </summary>
        public EditTransaction Begin(string description)
        {
            EnsureNoTransaction();
            _open = new EditTransaction(this, Document, description);
            return _open;
        }

        public bool Undo()
        {
            EnsureNoTransaction();
            if (!CanUndo)
            {
                return false;
            }
            MapDocument previous = Document;
            string description = _versions[_current].Description;
            _current--;
            Changed?.Invoke(this, new DocumentChange(previous, Document, description, ChangeReason.Undo));
            return true;
        }

        public bool Redo()
        {
            EnsureNoTransaction();
            if (!CanRedo)
            {
                return false;
            }
            MapDocument previous = Document;
            _current++;
            Changed?.Invoke(this, new DocumentChange(previous, Document, _versions[_current].Description, ChangeReason.Redo));
            return true;
        }

        /// <summary>
        /// Ends the open transaction, keeping its result unless it is null.
        /// </summary>
        internal bool End(EditTransaction transaction, MapDocument? result)
        {
            if (!ReferenceEquals(_open, transaction))
            {
                throw new InvalidOperationException("The transaction is not the open one");
            }
            _open = null;
            return result is not null && Record(transaction.Description, result);
        }

        private bool Record(string description, MapDocument result)
        {
            MapDocument previous = Document;
            if (result.Equals(previous))
            {
                return false;
            }

            // a new edit ends the versions that could be redone
            _versions.RemoveRange(_current + 1, _versions.Count - _current - 1);
            _versions.Add((result, description));
            if (_versions.Count > HistoryLimit + 1)
            {
                _versions.RemoveRange(0, _versions.Count - HistoryLimit - 1);
            }
            _current = _versions.Count - 1;

            Changed?.Invoke(this, new DocumentChange(previous, result, description, ChangeReason.Edit));
            return true;
        }

        private void EnsureNoTransaction()
        {
            if (_open is not null)
            {
                throw new InvalidOperationException($"The transaction '{_open.Description}' is still open");
            }
        }
    }
}
