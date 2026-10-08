namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// An army with the units it starts with.
    /// </summary>
    public sealed record ArmyDto
    {
        public required string Name { get; init; }

        public string? Personality { get; init; }

        public string? Plans { get; init; }

        public int? Color { get; init; }

        public int? Faction { get; init; }

        /// <summary>
        /// The resources the army starts with.
        /// </summary>
        public EconomyDto? Economy { get; init; }

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
