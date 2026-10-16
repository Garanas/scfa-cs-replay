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

        public required string Description { get; init; }

        public required string Author { get; init; }

        public required ScenarioType Type { get; init; }

        /// <summary>
        /// The path of a preview image, usually empty.
        /// </summary>
        public required string PreviewPath { get; init; }

        /// <summary>
        /// The radius around each start position that others may not enter while no rush lasts.
        /// </summary>
        public required double NoRushRadius { get; init; }

        /// <summary>
        /// Whether the map adapts its resources to the number of players.
        /// </summary>
        public required bool AdaptiveMap { get; init; }

        /// <summary>
        /// The mass and energy in the map's props, as the map editor computed them.
        /// </summary>
        public ReclaimDto? Reclaim { get; init; }

        /// <summary>
        /// The armies players take in the lobby, in order.
        /// </summary>
        public required IReadOnlyList<string> PlayableArmies { get; init; }

        /// <summary>
        /// The armies no player takes, e.g. <c>NEUTRAL_CIVILIAN</c>.
        /// </summary>
        public required IReadOnlyList<string> ExtraArmies { get; init; }

        /// <summary>
        /// The factions a playable army may play, for the armies that may not play every faction.
        /// </summary>
        public Dictionary<string, IReadOnlyList<string>>? ArmyFactions { get; init; }

        /// <summary>
        /// The teams the map maker puts playable armies in; absent for none.
        /// </summary>
        public IReadOnlyList<TeamDto>? Teams { get; init; }

        /// <summary>
        /// False for a map that may not go to the vault (the workspace's sandbox); left out (true)
        /// for every other map.
        /// </summary>
        public bool? Publishable { get; init; }

        /// <summary>
        /// The name of the area that is the playable area at the start; left out for the first
        /// area, the map editors' convention.
        /// </summary>
        public string? PlayableArea { get; init; }
    }
}
