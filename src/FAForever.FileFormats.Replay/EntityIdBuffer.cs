namespace FAForever.FileFormats.Replay
{
    /// <summary>
    /// Reads the selections of commands (<see cref="CommandUnits"/>) and stores their entity ids
    /// in shared chunks, so a selection costs a slice of a chunk instead of an array of its own.
    /// Players give many orders to the same selection in a row (a group of units, a factory), so
    /// a selection equal to the previous one of the same source is shared rather than stored
    /// again. One buffer lives for one replay body; the slices keep their chunks alive.
    /// </summary>
    internal sealed class EntityIdBuffer
    {
        private const int InitialChunkSize = 4 * 1024;
        private const int MaxChunkSize = 64 * 1024;

        private static readonly CommandUnits NoUnits = new CommandUnits(0);

        private int[] chunk = [];
        private int used;

        /// <summary>The last selection read per source.</summary>
        private readonly Dictionary<int, CommandUnits> lastBySource = new Dictionary<int, CommandUnits>();

        /// <summary>
        /// Reads a selection: its entity count followed by that many entity ids.
        /// </summary>
        public CommandUnits Read(ReplayBinaryReader reader, int source)
        {
            int count = reader.ReadInt32();
            if (count <= 0)
            {
                return NoUnits;
            }

            if (used + count > chunk.Length)
            {
                // A new chunk (the first one lazily, so a body without commands costs nothing); the
                // previous one stays referenced by the slices handed out.
                chunk = new int[Math.Max(count, Math.Min(Math.Max(chunk.Length * 2, InitialChunkSize), MaxChunkSize))];
                used = 0;
            }

            Span<int> ids = chunk.AsSpan(used, count);
            reader.ReadInt32s(ids);

            if (lastBySource.TryGetValue(source, out CommandUnits? previous) && previous.EntityIds.Span.SequenceEqual(ids))
            {
                // The same selection again: share it, and leave the ids just read to be overwritten.
                return previous;
            }

            CommandUnits units = new CommandUnits(count) { EntityIds = new ReadOnlyMemory<int>(chunk, used, count) };
            used += count;
            lastBySource[source] = units;
            return units;
        }
    }
}
