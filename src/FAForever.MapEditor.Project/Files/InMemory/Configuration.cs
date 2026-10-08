using System.Collections.Immutable;

namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// A configuration of the scenario: its teams and custom properties.
    /// </summary>
    public sealed record Configuration
    {
        public required string Name { get; init; }

        public ImmutableArray<Team> Teams { get; init; } = [];

        /// <summary>
        /// E.g. <c>ExtraArmies</c>: the armies no player takes.
        /// </summary>
        public ImmutableSortedDictionary<string, string> CustomProperties { get; init; } = ImmutableSortedDictionary.Create<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// The factions each army may play, in the order of the armies; absent when any faction may play.
        /// </summary>
        public ImmutableArray<ImmutableArray<string>>? Factions { get; init; }
    }
}
