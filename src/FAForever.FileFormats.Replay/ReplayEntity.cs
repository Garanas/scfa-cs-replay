namespace FAForever.FileFormats.Replay
{
    /// <summary>
    /// What kind of unit an entity probably is, judged from the orders it received. Only
    /// <see cref="Commander"/> is close to certain (serial 0 of an army); the others are guesses.
    /// </summary>
    public enum ReplayEntityKind
    {
        /// <summary>The army's commander (ACU): entity serial 0. The serial holds until the commander is
        /// destroyed; in game modes that continue after that, a later unit of the army takes it.</summary>
        Commander,

        /// <summary>Received construction, reclaim, repair or capture orders.</summary>
        Engineer,

        /// <summary>Received factory orders: build queues or orders for what it builds.</summary>
        Factory,

        /// <summary>Only received upgrade orders, e.g. a mass extractor.</summary>
        Structure,

        /// <summary>Anything else: units that move and fight.</summary>
        Unit,
    }

    /// <summary>
    /// One order as one entity received it. The same order object is shared by every entity of
    /// the selection it went to.
    /// </summary>
    /// <param name="Queued">Shift-queued after the entity's previous order (not
    /// <see cref="CommandData.ClearQueue"/>); an order that is not queued replaces the queue, so
    /// it starts a new chain.</param>
    /// <param name="SelectionSize">The number of entities the order went to.</param>
    public record ReplayEntityOrder(
        TimeSpan Timestamp,
        CommandType CommandType,
        ReplayAnalysis.MapPosition? Position,
        string? BlueprintId,
        bool Queued,
        bool FromFactory,
        int SelectionSize);

    /// <summary>
    /// An entity id that received orders, with those orders in time order. See AGENTS.md for the
    /// layout of entity ids: <c>(army index &lt;&lt; 20) | serial</c>. The engine reuses the id of a
    /// destroyed unit for a later unit of the same army, so in a long game one entity can hold the
    /// orders of several units; the replay does not say when a unit died.
    /// </summary>
    public record ReplayEntity(
        int EntityId,
        int SourceId,
        ReplayEntityKind Kind,
        IReadOnlyList<ReplayEntityOrder> Orders)
    {
        /// <summary>The army index encoded in the entity id.</summary>
        public int Army => ReplayEntityId.GetIndex(EntityId);

        /// <summary>The family encoded in the entity id: <see cref="ReplayEntityFamily.Unit"/> for selections.</summary>
        public ReplayEntityFamily Family => ReplayEntityId.GetFamily(EntityId);

        /// <summary>
        /// The serial number within the army. The army's first unit (its commander at the start)
        /// has serial 0; a new unit takes the lowest serial that is free, which is a serial of a unit
        /// destroyed at least about 10 seconds earlier when there is one, so the serial does not tell
        /// the order in which units appeared.
        /// </summary>
        public int Serial => ReplayEntityId.GetSerial(EntityId);

        /// <summary>When the entity received its first order.</summary>
        public TimeSpan FirstSeen => Orders[0].Timestamp;

        /// <summary>
        /// Whether the order at <paramref name="index"/> continues the chain of the order before it:
        /// it was queued after it, and that one was not a stop.
        /// </summary>
        public bool ContinuesChain(int index)
            => index > 0 && Orders[index].Queued && Orders[index - 1].CommandType != CommandType.IssueStop;
    }
}
