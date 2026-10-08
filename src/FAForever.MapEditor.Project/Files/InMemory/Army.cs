using System.Collections.Immutable;

namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// An army with the units it starts with. Its economy (0 mass and energy in every map of the
    /// vault) and personality (always empty) are left to the export.
    /// </summary>
    public sealed record Army
    {
        public required string Name { get; init; }

        /// <summary>
        /// The AI plans of a co-op army, a path into the map's folder; null for none.
        /// </summary>
        public string? Plans { get; init; }

        public int Color { get; init; }

        public int Faction { get; init; }

        /// <summary>
        /// The stance towards other armies by name, e.g. <c>Enemy</c>.
        /// </summary>
        public ImmutableSortedDictionary<string, string> Alliances { get; init; } = ImmutableSortedDictionary.Create<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// The army's root group, usually holding the group <c>INITIAL</c>.
        /// </summary>
        public required UnitGroup Units { get; init; }
    }
}
