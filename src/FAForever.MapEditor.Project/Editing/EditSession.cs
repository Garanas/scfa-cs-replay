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

        /// <summary>
        /// A result derived from the document (a generated normal map, masks from rules) went into the
        /// current version without a step of undo (<see cref="EditSession.Derive"/>).
        /// </summary>
        Derive,
    }

    /// <summary>
    /// The document before and after a change. Parts that did not change are the same objects in
    /// both, so a view finds what to redraw by comparing references, and the tiles of a grid with
    /// <see cref="TiledBuffer{T}.ChangedTiles"/>.
    /// </summary>
    /// <param name="Description">The edit that was made, undone, redone or derived, e.g. <c>Raise terrain</c>.</param>
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
    /// What is derived from the document (generated results that are part of the map) goes into the
    /// current version without a step of its own (<see cref="Derive"/>): undo returns the version
    /// before with the results derived for it.
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
        private EditGroup? _group;
        private string? _mergeKey;
        private MapDocument _saved;
        private readonly List<(string Description, Func<MapDocument, MapDocument> Edit)> _derived = [];

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

        /// <summary>
        /// Whether a step can be undone: there is one, and no group is open (<see cref="BeginGroup"/>).
        /// </summary>
        public bool CanUndo => _current > 0 && _group is null;

        public bool CanRedo => _current < _versions.Count - 1 && _group is null;

        /// <summary>
        /// Whether a group is open (<see cref="BeginGroup"/>): its steps become one when it ends.
        /// </summary>
        public bool InGroup => _group is not null;

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
        /// Raised after every edit, undo, redo and derived change.
        /// </summary>
        public event EventHandler<DocumentChange>? Changed;

        /// <summary>
        /// Raised when <see cref="IsModified"/> may have changed: after edits, undo, redo and saves.
        /// </summary>
        public event EventHandler? StateChanged;

        /// <summary>
        /// The height layer (user story T-5) that changes of the heightmap itself go into while the
        /// map has layers: the one the sculpt tool writes into. Every step keeps the heightmap the
        /// sum of the layers (<see cref="HeightLayers.Follow"/>); without a layer here that can be
        /// sculpted, a change of the heightmap itself is undone by that.
        /// </summary>
        public string? SculptLayer { get; set; }

        /// <summary>
        /// Where the decal sets' mask pictures are read from, for the decal-masks layers that follow
        /// the decals after every step (<see cref="DecalMaskLayers.Follow(MapDocument, MapDocument, IDecalMaskPictures?)"/>);
        /// null leaves those layers as they are.
        /// </summary>
        public IDecalMaskPictures? DecalPictures { get; set; }

        /// <summary>
        /// The heightmap brushes write (the one the editor shows): the final, or the draft. While the
        /// map has a draft of its own, <see cref="EditTransaction.Heightmap"/> hands out the draft's
        /// samples for the draft, and what a brush writes goes into <see cref="SculptLayer"/> of the
        /// draft's stack.
        /// </summary>
        public HeightStackKind Heights { get; set; }

        /// <summary>
        /// Per stack of coverage (by target key, <c>stratum-2</c>, <c>mask-forest</c>, <c>water-foam</c>)
        /// the painted layer that changes of its grid go into: the one the strata, mask or water brush
        /// paints. Without one (or one that cannot be painted) the highest that can is painted
        /// (<see cref="CoverageLayers.Follow"/>).
        /// </summary>
        public Dictionary<string, string> PaintLayers { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// The painted layer chosen for a stack of coverage, or null.
        /// </summary>
        internal string? PaintLayerOf(string key) => PaintLayers.GetValueOrDefault(key);

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
        /// Puts what is derived from the document into the current version without a step of undo: a
        /// generated result that is part of the map (the normal map, the preview, masks or props from
        /// rules), whether asked for or made on its own. The version keeps it, so an undo returns the
        /// version before with the results derived for that one, and a redo this one with its own.
        /// The map counts as modified; <see cref="Changed"/> is raised with
        /// <see cref="ChangeReason.Derive"/>. The history's steps, and the steps that can be redone,
        /// stay as they were. While a transaction is open (a brush stroke) it waits until the
        /// transaction ends, and is then made on the document the transaction left.
        /// </summary>
        /// <returns>Whether the document changed now; false also when it waits for the transaction.</returns>
        public bool Derive(string description, Func<MapDocument, MapDocument> edit)
        {
            if (_open is not null)
            {
                _derived.Add((description, edit));
                return false;
            }
            return Replace(description, edit(Document));
        }

        /// <summary>
        /// How many derived changes wait for the open transaction to end (<see cref="Derive"/>).
        /// </summary>
        public int DerivedWaiting => _derived.Count;

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

        /// <summary>
        /// Starts a group: the steps made until it ends (edits, transactions, in any number) become
        /// one step when it is committed, and are all undone when it is thrown away, as a plugin
        /// run that runs other commands. Undo and redo wait until it ends; the history keeps every
        /// step of the group meanwhile, whatever <see cref="HistoryLimit"/>.
        /// </summary>
        /// <exception cref="InvalidOperationException">A transaction or another group is open.</exception>
        public EditGroup BeginGroup(string description)
        {
            EnsureNoTransaction();
            if (_group is not null)
            {
                throw new InvalidOperationException($"The group '{_group.Description}' is still open");
            }
            _group = new EditGroup(this, description, _current);
            _mergeKey = null;
            return _group;
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
        /// Undoes or redoes up to a step of <see cref="History"/> in one go, as a history panel does
        /// when a step is clicked; -1 goes back to the document the session started with (or the
        /// oldest it remembers).
        /// </summary>
        /// <returns>Whether the document changed.</returns>
        public bool GoTo(int step)
        {
            EnsureNoTransaction();
            if (_group is not null)
            {
                return false;
            }
            ArgumentOutOfRangeException.ThrowIfLessThan(step, -1);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(step, _versions.Count - 1);
            int target = step + 1;
            if (target == _current)
            {
                return false;
            }

            MapDocument previous = Document;
            bool back = target < _current;
            string description = back ? _versions[_current].Description : _versions[target].Description;
            if (back && target + 1 < _current)
            {
                description = $"{_current - target} steps";
            }
            else if (!back && target - 1 > _current)
            {
                description = $"{target - _current} steps";
            }
            _current = target;
            _mergeKey = null;
            Raise(new DocumentChange(previous, Document, description, back ? ChangeReason.Undo : ChangeReason.Redo));
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
            bool changed = result is not null && Record(transaction.Description, result, mergeKey: null);
            DeriveWaiting();
            return changed;
        }

        // the derived changes that waited for a transaction, on the document it left
        private void DeriveWaiting()
        {
            (string Description, Func<MapDocument, MapDocument> Edit)[] waiting = [.. _derived];
            _derived.Clear();
            foreach ((string description, Func<MapDocument, MapDocument> edit) in waiting)
            {
                Replace(description, edit(Document));
            }
        }

        /// <summary>
        /// Ends the open group: its steps become one (<paramref name="keep"/>), or are undone and
        /// forgotten. A transaction still open in it is thrown away first.
        /// </summary>
        /// <returns>Whether the group's steps changed the document and were kept.</returns>
        internal bool EndGroup(EditGroup group, bool keep)
        {
            if (!ReferenceEquals(_group, group))
            {
                throw new InvalidOperationException("The group is not the open one");
            }
            _open?.Dispose();
            _group = null;
            _mergeKey = null;
            int start = Math.Min(group.Start, _current);
            if (_current == start)
            {
                return false;
            }

            MapDocument last = Document;
            MapDocument first = _versions[start].Document;
            _versions.RemoveRange(start + 1, _versions.Count - start - 1);
            _current = start;
            if (!keep || last.Equals(first))
            {
                // thrown away, or the steps came to nothing: the document as the group found it
                Raise(new DocumentChange(last, first, group.Description, keep ? ChangeReason.Edit : ChangeReason.Undo));
                return false;
            }
            _versions.Add(new Version(last, group.Description));
            if (_versions.Count > HistoryLimit + 1)
            {
                _versions.RemoveRange(0, _versions.Count - HistoryLimit - 1);
            }
            _current = _versions.Count - 1;
            // the document is the one the last step made: only the history changed
            StateChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        private bool Record(string description, MapDocument result, string? mergeKey)
        {
            MapDocument previous = Document;
            result = DecalMaskLayers.Follow(previous, result, DecalPictures);
            // the grids of coverage first: height layers may be clipped by their composites
            result = CoverageLayers.Follow(previous, result, PaintLayerOf);
            result = HeightLayers.Follow(previous, result, SculptLayer);
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
                if (_versions.Count > HistoryLimit + 1 && _group is null)
                {
                    _versions.RemoveRange(0, _versions.Count - HistoryLimit - 1);
                }
            }
            _current = _versions.Count - 1;
            _mergeKey = mergeKey;

            Raise(new DocumentChange(previous, result, description, ChangeReason.Edit));
            return true;
        }

        private bool Replace(string description, MapDocument result)
        {
            MapDocument previous = Document;
            result = DecalMaskLayers.Follow(previous, result, DecalPictures);
            result = CoverageLayers.Follow(previous, result, PaintLayerOf);
            result = HeightLayers.Follow(previous, result, SculptLayer);
            if (result.Equals(previous))
            {
                return false;
            }
            _versions[_current] = _versions[_current] with { Document = result };
            Raise(new DocumentChange(previous, result, description, ChangeReason.Derive));
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
