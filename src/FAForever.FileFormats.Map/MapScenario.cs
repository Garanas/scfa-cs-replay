using FAForever.FileFormats.Lua;

namespace FAForever.FileFormats.Map
{
    /// <summary>
    /// What a map's <c>_scenario.lua</c> says about it: its name, size, files and the armies of its
    /// teams. The fields are named after the keys of <c>ScenarioInfo</c>; the full table is in
    /// <see cref="Raw"/>.
    /// </summary>
    public sealed record MapScenario
    {
        /// <summary>
        /// The keys of <see cref="Raw"/> that a property holds; the others, such as
        /// <c>norushoffsetX_ARMY_1</c>, have none.
        /// </summary>
        public static IReadOnlySet<string> TypedKeys { get; } = new HashSet<string>
        {
            "name", "description", "preview", "map_version", "AdaptiveMap", "type", "starts", "size",
            "reclaim", "map", "save", "script", "norushradius", "Configurations",
        };

        public required LuaData.Table Raw { get; init; }

        public required string Name { get; init; }

        public string? Description { get; init; }

        /// <summary>
        /// <c>skirmish</c> for the maps players play on, <c>campaign_coop</c> for co-op missions.
        /// </summary>
        public string? Type { get; init; }

        /// <summary>
        /// Whether the armies start at the map's start positions.
        /// </summary>
        public bool? Starts { get; init; }

        public string? Preview { get; init; }

        /// <summary>
        /// The size in game units (<c>size = {512, 512}</c>).
        /// </summary>
        public required (int Width, int Height) Size { get; init; }

        /// <summary>
        /// The path of the binary map, e.g. <c>/maps/abhor.v0004/abhor.scmap</c>.
        /// </summary>
        public string? Map { get; init; }

        /// <summary>
        /// The path of the <c>_save.lua</c>.
        /// </summary>
        public string? Save { get; init; }

        public string? Script { get; init; }

        /// <summary>
        /// The version of the map in the vault (<c>map_version</c>), the number after <c>.v</c> in its folder.
        /// </summary>
        public int? MapVersion { get; init; }

        public double? NoRushRadius { get; init; }

        /// <summary>
        /// The mass and energy in the map's props, as the map editor computed them (<c>reclaim</c>).
        /// </summary>
        public (double Mass, double Energy)? Reclaim { get; init; }

        /// <summary>
        /// Whether the map adapts its resources to the number of players (<c>AdaptiveMap</c>).
        /// </summary>
        public bool? AdaptiveMap { get; init; }

        /// <summary>
        /// The configurations, by name; skirmish maps have one, <c>standard</c>.
        /// </summary>
        public required IReadOnlyList<MapConfiguration> Configurations { get; init; }

        /// <summary>
        /// The armies players can take in the <c>standard</c> configuration (or the first), in order:
        /// <c>ARMY_1</c>, <c>ARMY_2</c>, ... The game's lobby takes them from the first team, <c>FFA</c>;
        /// teams after it (which a map editor may write) repeat some of them. A configuration whose
        /// first team is no <c>FFA</c> has the armies of all its teams.
        /// </summary>
        public IReadOnlyList<string> PlayerArmies =>
            (Configurations.FirstOrDefault(c => c.Name == "standard") ?? Configurations.FirstOrDefault()) is MapConfiguration configuration
                ? configuration.Teams.Count > 0 && configuration.Teams[0].Name == "FFA"
                    ? [.. configuration.Teams[0].Armies]
                    : [.. configuration.Teams.SelectMany(team => team.Armies).Distinct(StringComparer.Ordinal)]
                : [];
    }

    /// <summary>
    /// A configuration of a scenario: its teams and the custom properties.
    /// </summary>
    /// <param name="CustomProperties">The <c>customprops</c>, e.g. <c>ExtraArmies</c>.</param>
    /// <param name="Raw">The configuration's table, with the keys that have no property (e.g. <c>factions</c>).</param>
    public sealed record MapConfiguration(string Name, IReadOnlyList<MapTeam> Teams, IReadOnlyDictionary<string, string> CustomProperties, LuaData.Table Raw)
    {
        /// <summary>
        /// The keys of <see cref="Raw"/> that a property holds.
        /// </summary>
        public static IReadOnlySet<string> TypedKeys { get; } = new HashSet<string> { "teams", "customprops" };

        /// <summary>
        /// The armies that no player takes, e.g. <c>ARMY_9</c> and <c>NEUTRAL_CIVILIAN</c>
        /// (<c>customprops.ExtraArmies</c>, separated by spaces).
        /// </summary>
        public IReadOnlyList<string> ExtraArmies =>
            CustomProperties.TryGetValue("ExtraArmies", out string? armies)
                ? armies.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                : [];
    }

    /// <param name="Name">The team's name; skirmish maps have one team, <c>FFA</c>.</param>
    public sealed record MapTeam(string Name, IReadOnlyList<string> Armies);

    /// <summary>
    /// Reads a map's <c>_scenario.lua</c>.
    /// </summary>
    public static class MapScenarioParser
    {
        /// <summary>
        /// Runs the file and reads the <c>ScenarioInfo</c> it assigns.
        /// </summary>
        /// <exception cref="LuaSyntaxException">The file is not valid Lua.</exception>
        /// <exception cref="FormatException">The file does not assign <c>ScenarioInfo</c>.</exception>
        public static MapScenario Parse(string source)
        {
            IReadOnlyDictionary<string, LuaData> globals = LuaSourceParser.Execute(source, MapLuaFunctions.All);
            if (!globals.TryGetValue("ScenarioInfo", out LuaData? value) || value is not LuaData.Table info)
            {
                throw new FormatException("The scenario does not assign ScenarioInfo");
            }

            LuaTableReader t = new LuaTableReader(info);
            IReadOnlyList<double> size = t.Numbers("size");
            IReadOnlyList<double> reclaim = t.Numbers("reclaim");

            return new MapScenario
            {
                Raw = info,
                Name = t.String("name") ?? "",
                Description = t.String("description"),
                Type = t.String("type"),
                Starts = t.Bool("starts"),
                Preview = t.String("preview"),
                Size = size.Count >= 2 ? ((int)size[0], (int)size[1]) : (0, 0),
                Map = t.String("map"),
                Save = t.String("save"),
                Script = t.String("script"),
                MapVersion = t.Integer("map_version"),
                NoRushRadius = t.Number("norushradius"),
                Reclaim = reclaim.Count >= 2 ? (reclaim[0], reclaim[1]) : null,
                AdaptiveMap = t.Bool("AdaptiveMap"),
                Configurations = t.Dictionary("Configurations", configuration => configuration)
                    .Select(configuration => new MapConfiguration(
                        configuration.Key,
                        configuration.Value.List("teams", team => new MapTeam(team.String("name") ?? "", team.Strings("armies"))),
                        configuration.Value.StringDictionary("customprops"),
                        configuration.Value.Table))
                    .ToList(),
            };
        }
    }
}
