using System.Collections.Immutable;

namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// A named group of decals.
    /// </summary>
    public sealed record DecalGroup
    {
        public int Id { get; init; }

        public required string Name { get; init; }

        public ImmutableArray<int> DecalIds { get; init; } = [];
    }
}
