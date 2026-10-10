using System.Collections.Immutable;

namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// What the scenario says about the map, apart from the paths of its files and its size.
    /// </summary>
    /// <remarks>
    /// Every map of the vault has one configuration, <c>standard</c>, with one team, <c>FFA</c>, of
    /// the armies players take, and the other armies in <c>customprops.ExtraArmies</c>. The editor
    /// keeps the two lists of armies; the export builds the configuration.
    /// </remarks>
    public sealed record ScenarioInfo
    {
        /// <summary>
        /// The name players see.
        /// </summary>
        public required string Name { get; init; }

        public string Description { get; init; } = "";

        public string Author { get; init; } = "";

        public ScenarioType Type { get; init; } = ScenarioType.Skirmish;

        /// <summary>
        /// The path of a preview image, usually empty.
        /// </summary>
        public string PreviewPath { get; init; } = "";

        /// <summary>
        /// The radius around each start position that others may not enter while no rush lasts.
        /// </summary>
        public double NoRushRadius { get; init; }

        /// <summary>
        /// Whether the map adapts its resources to the number of players.
        /// </summary>
        public bool AdaptiveMap { get; init; }

        /// <summary>
        /// The mass and energy in the map's props, as the map editor computed them.
        /// </summary>
        public Reclaim? Reclaim { get; init; }

        /// <summary>
        /// The armies players take in the lobby, in order: <c>ARMY_1</c>, <c>ARMY_2</c>, ...
        /// </summary>
        public ImmutableArray<string> PlayableArmies { get; init; } = [];

        /// <summary>
        /// The armies no player takes, e.g. <c>ARMY_9</c> and <c>NEUTRAL_CIVILIAN</c>.
        /// </summary>
        public ImmutableArray<string> ExtraArmies { get; init; } = [];

        /// <summary>
        /// The factions a playable army may play, for the armies that may not play every faction
        /// (some co-op missions).
        /// </summary>
        public ImmutableSortedDictionary<string, ImmutableArray<string>> ArmyFactions { get; init; } =
            ImmutableSortedDictionary.Create<string, ImmutableArray<string>>(StringComparer.Ordinal);

        /// <summary>
        /// Whether the map may go to the vault: false for the workspace's sandbox
        /// (<see cref="Workspaces.SandboxMap"/>), which is for trying the editor out. It stays in the
        /// project only; the game's files do not carry it.
        /// </summary>
        public bool Publishable { get; init; } = true;
    }
}
