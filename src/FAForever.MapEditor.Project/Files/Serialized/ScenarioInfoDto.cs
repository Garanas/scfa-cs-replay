namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// What the scenario says about the map, apart from the paths of its files and its size.
    /// </summary>
    public sealed record ScenarioInfoDto
    {
        /// <summary>
        /// The name players see.
        /// </summary>
        public required string Name { get; init; }

        public string? Description { get; init; }

        public string? Author { get; init; }

        /// <summary>
        /// <c>skirmish</c> for the maps players play on, <c>campaign_coop</c> for co-op missions.
        /// </summary>
        public string? Type { get; init; }

        /// <summary>
        /// Whether the armies start at the map's start positions.
        /// </summary>
        public bool? Starts { get; init; }

        /// <summary>
        /// The path of a preview image, usually empty.
        /// </summary>
        public string? PreviewPath { get; init; }

        /// <summary>
        /// The radius around each start position that others may not enter while no rush lasts.
        /// </summary>
        public double? NoRushRadius { get; init; }

        /// <summary>
        /// Where the no-rush circle of an army lies relative to its start position, by army.
        /// </summary>
        public required Dictionary<string, NoRushOffsetDto> NoRushOffsets { get; init; }

        /// <summary>
        /// Whether the map adapts its resources to the number of players.
        /// </summary>
        public bool? AdaptiveMap { get; init; }

        /// <summary>
        /// The mass and energy in the map's props, as the map editor computed them.
        /// </summary>
        public ReclaimDto? Reclaim { get; init; }

        /// <summary>
        /// The configurations; skirmish maps have one, <c>standard</c>.
        /// </summary>
        public required IReadOnlyList<ConfigurationDto> Configurations { get; init; }
    }
}
