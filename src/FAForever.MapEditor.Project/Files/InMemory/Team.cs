using System.Collections.Immutable;

namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// A team the map maker puts playable armies in, e.g. <c>North</c> with <c>ARMY_1</c> and
    /// <c>ARMY_3</c>.
    /// </summary>
    /// <remarks>
    /// The game's lobby reads only the first team of the scenario's <c>standard</c> configuration,
    /// which must be <c>FFA</c> with every playable army; the export writes that one first and the
    /// map maker's teams after it, where the game leaves them alone and the import finds them again.
    /// </remarks>
    public sealed record Team
    {
        public required string Name { get; init; }

        /// <summary>
        /// The playable armies in the team, in order.
        /// </summary>
        public ImmutableArray<string> Armies { get; init; } = [];
    }
}
