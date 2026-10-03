
namespace FAForever.Replay
{
    /// <summary>
    /// The selection an input applies to: the number of entities and their ids. Entity ids are
    /// assigned by the simulation and stable for the lifetime of a unit, so they link the orders
    /// given to the same unit; what kind of unit an id is, the replay does not say.
    /// </summary>
    public record CommandUnits(int UnitCount)
    {
        /// <summary>The entity ids of the selection, <see cref="UnitCount"/> of them.</summary>
        public ReadOnlyMemory<int> EntityIds { get; init; } = ReadOnlyMemory<int>.Empty;
    }
}
