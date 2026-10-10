namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// Steps of an <see cref="EditSession"/> that become one step when committed, such as a plugin
    /// run that edits the map and runs commands that are each a step (<see cref="EditSession.BeginGroup"/>).
    /// Disposing it without <see cref="Commit"/> undoes its steps and forgets them, so a failing
    /// plugin changes nothing.
    /// </summary>
    /// <remarks>
    /// Unlike a transaction, a group lets each step be made as usual (an edit, a transaction of a
    /// command), and the document is each step's while the group is open, so what a step reads is
    /// what the steps before it made.
    /// </remarks>
    public sealed class EditGroup : IDisposable
    {
        private readonly EditSession _session;
        private bool _ended;

        internal EditGroup(EditSession session, string description, int start)
        {
            _session = session;
            Description = description;
            Start = start;
        }

        /// <summary>
        /// The one step's description, e.g. <c>Scatter rocks: Scatter on steep slopes</c>.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// The version of the history the group started at.
        /// </summary>
        internal int Start { get; }

        /// <summary>
        /// Keeps the group's steps as one step of the history.
        /// </summary>
        /// <returns>Whether the document changed.</returns>
        public bool Commit()
        {
            ObjectDisposedException.ThrowIf(_ended, this);
            _ended = true;
            return _session.EndGroup(this, keep: true);
        }

        /// <summary>
        /// Undoes the group's steps and forgets them, unless it was committed.
        /// </summary>
        public void Dispose()
        {
            if (!_ended)
            {
                _ended = true;
                _session.EndGroup(this, keep: false);
            }
        }
    }
}
