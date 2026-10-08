namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// An army with the units it starts with.
    /// </summary>
    public sealed record ArmyDto
    {
        public required string Name { get; init; }

        /// <summary>
        /// The AI plans of a co-op army, a path into the map's folder.
        /// </summary>
        public string? Plans { get; init; }

        public required int Color { get; init; }

        public required int Faction { get; init; }

        /// <summary>
        /// The stance towards other armies by name, e.g. <c>Enemy</c>.
        /// </summary>
        public required Dictionary<string, string> Alliances { get; init; }

        /// <summary>
        /// The army's root group, usually holding the group <c>INITIAL</c>.
        /// </summary>
        public required UnitGroupDto Units { get; init; }
    }
}
