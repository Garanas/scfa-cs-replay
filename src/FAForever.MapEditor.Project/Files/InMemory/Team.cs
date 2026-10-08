using System.Collections.Immutable;

namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// A team and the armies in it.
    /// </summary>
    public sealed record Team
    {
        /// <summary>
        /// The team's name; skirmish maps have one team, <c>FFA</c>.
        /// </summary>
        public required string Name { get; init; }

        /// <summary>
        /// E.g. <c>ARMY_1</c>, <c>ARMY_2</c>.
        /// </summary>
        public ImmutableArray<string> Armies { get; init; } = [];
    }
}
