using System.Collections.Immutable;

namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// A named group of units and further groups.
    /// </summary>
    public sealed record UnitGroup
    {
        public required string Name { get; init; }

        public ImmutableArray<UnitGroup> Groups { get; init; } = [];

        public ImmutableArray<Unit> Units { get; init; } = [];
    }
}
