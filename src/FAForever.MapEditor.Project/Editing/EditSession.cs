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
    /// A step of the history, for a history panel.
    /// </summary>
    /// <param name="IsCurrent">Whether the document is at this step: the steps after it can be redone.</param>
    public sealed record HistoryStep(string Description, bool IsCurrent);

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
    /// Edits with the same merge key that follow each other become one step: dragging a slider
    /// makes one undo step, not one per value it passed. Anything else in between (another edit,
    /// an undo, a save) starts a new step.
    /// </para>
    /// <para>
    /// A session is not thread safe: use it from one thread, the user interface's.
    /// </para>
    /// </remarks>
    public sealed class EditSession
    {
        private readonly List<Version> _versions;
        private int _current;
        private EditTransaction? _open;
        private string? _mergeKey;
        private MapDocument _saved;

        /// <param name="historyLimit">How many edits can be undone; older ones are forgotten.</param>
        public EditSession(MapDocument document, int historyLimit = 200)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(historyLimit);
            _versions = [new Version(document, "")];
            _saved = document;
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
        /// The edits that can be undone and redone, oldest first; the document the session started
        /// with (or the oldest it remembers) is not a step.
        /// </summary>
        public IReadOnlyList<HistoryStep> History =>
            [.. _versions.Skip(1).Select((version, index) => new HistoryStep(version.Description, index + 1 == _current))];

        /// <summary>
        /// Whether the document differs from the one last saved (<see cref="MarkSaved"/>), or from the
        /// one the session started with.
        /// </summary>
        public bool IsModified => !ReferenceEquals(Document, _saved);

        /// <summary>
        /// Whether a transaction is open; no other change can be made until it ends.
        /// </summary>
        public bool InTransaction => _open is not null;

        /// <summary>
        /// Raised after every edit, undo and redo.
        /// </summary>
        public event EventHandler<DocumentChange>? Changed;

        /// <summary>
        /// Raised when <see cref="IsModified"/> may have changed: after edits, undo, redo and saves.
        /// </summary>
        public event EventHandler? StateChanged;

        /// <summary>
        /// Makes one edit, which can be undone as one step.
        /// </summary>
        /// <param name="mergeKey">Merges the edit into the previous step when that was an edit
        /// with the same key, e.g. <c>water.elevation</c> while a slider is dragged.</param>
        /// <returns>Whether the document changed; an edit that changes nothing is not kept.</returns>
        public bool Edit(string description, Func<MapDocument, MapDocument> edit, string? mergeKey = null)
        {
            EnsureNoTransaction();
            return Record(description, edit(Document), mergeKey);
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
            _mergeKey = null;
            Raise(new DocumentChange(previous, Document, description, ChangeReason.Undo));
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
            _mergeKey = null;
            Raise(new DocumentChange(previous, Document, _versions[_current].Description, ChangeReason.Redo));
            return true;
        }

        /// <summary>
        /// Notes that a version of the document is the one on disk: the current one, or the one a
        /// save in the background wrote while the editing went on.
        /// </summary>
        public void MarkSaved(MapDocument? saved = null)
        {
            _saved = saved ?? Document;
            _mergeKey = null;
            StateChanged?.Invoke(this, EventArgs.Empty);
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
            return result is not null && Record(transaction.Description, result, mergeKey: null);
        }

        private bool Record(string description, MapDocument result, string? mergeKey)
        {
            MapDocument previous = Document;
            if (result.Equals(previous))
            {
                return false;
            }

            // a new edit ends the versions that could be redone
            _versions.RemoveRange(_current + 1, _versions.Count - _current - 1);
            if (mergeKey is not null && mergeKey == _mergeKey && _current > 0)
            {
                _versions[_current] = _versions[_current] with { Document = result };
            }
            else
            {
                _versions.Add(new Version(result, description));
                if (_versions.Count > HistoryLimit + 1)
                {
                    _versions.RemoveRange(0, _versions.Count - HistoryLimit - 1);
                }
            }
            _current = _versions.Count - 1;
            _mergeKey = mergeKey;

            Raise(new DocumentChange(previous, result, description, ChangeReason.Edit));
            return true;
        }

        private void Raise(DocumentChange change)
        {
            Changed?.Invoke(this, change);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void EnsureNoTransaction()
        {
            if (_open is not null)
            {
                throw new InvalidOperationException($"The transaction '{_open.Description}' is still open");
            }
        }

        private sealed record Version(MapDocument Document, string Description);
    }
}
