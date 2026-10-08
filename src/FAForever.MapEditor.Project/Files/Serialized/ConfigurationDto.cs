namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// A configuration of the scenario: its teams and custom properties.
    /// </summary>
    public sealed record ConfigurationDto
    {
        public required string Name { get; init; }

        public required IReadOnlyList<TeamDto> Teams { get; init; }

        /// <summary>
        /// E.g. <c>ExtraArmies</c>: the armies no player takes.
        /// </summary>
        public required Dictionary<string, string> CustomProperties { get; init; }

        /// <summary>
        /// The factions each army may play, in the order of the armies; absent when any faction may play.
        /// </summary>
        public IReadOnlyList<IReadOnlyList<string>>? Factions { get; init; }
    }
}
