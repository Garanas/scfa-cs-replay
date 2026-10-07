using System.Numerics;
using System.Text.RegularExpressions;
using FAForever.FileFormats.Lua;

namespace FAForever.FileFormats.Map
{
    /// <summary>
    /// What a map's <c>_save.lua</c> holds: its markers (start positions, mass and hydrocarbon
    /// deposits, AI path nodes, ...), areas, marker chains and armies with the units they start
    /// with. The full <c>Scenario</c> table is in <see cref="Raw"/>.
    /// </summary>
    /// <remarks>
    /// The game runs the file in <c>lua/SimInit.lua</c> and reads the markers from
    /// <c>Scenario.MasterChain._MASTERCHAIN_.Markers</c> (<c>lua/sim/ScenarioUtilities.lua</c>).
    /// </remarks>
    public sealed record MapSave
    {
        public required LuaData.Table Raw { get; init; }

        /// <summary>
        /// The markers in the order of the file.
        /// </summary>
        public required IReadOnlyList<MapMarker> Markers { get; init; }

        /// <summary>
        /// The named rectangles, e.g. the playable area of a co-op mission.
        /// </summary>
        public required IReadOnlyList<MapArea> Areas { get; init; }

        /// <summary>
        /// The named lists of markers, e.g. patrol routes.
        /// </summary>
        public required IReadOnlyList<MapChain> Chains { get; init; }

        public required IReadOnlyList<MapArmy> Armies { get; init; }

        /// <summary>
        /// The marker with this name, e.g. <c>ARMY_1</c> for the first start position.
        /// </summary>
        public MapMarker? GetMarker(string name) => Markers.FirstOrDefault(marker => marker.Name == name);

        /// <summary>
        /// The markers of one type, e.g. <c>Mass</c>, <c>Hydrocarbon</c> or <c>Land Path Node</c>.
        /// </summary>
        public IEnumerable<MapMarker> GetMarkers(string type) => Markers.Where(marker => marker.Type == type);
    }

    /// <summary>
    /// A marker. Start positions are the markers named after an army (<c>ARMY_1</c>, ...), of type
    /// <c>Blank Marker</c>.
    /// </summary>
    /// <param name="Orientation">The rotation around each axis, in radians.</param>
    /// <param name="Raw">The marker's table, with the fields of its type such as <c>amount</c>.</param>
    public sealed record MapMarker(string Name, string Type, Vector3 Position, Vector3 Orientation, LuaData.Table Raw);

    /// <summary>
    /// A rectangle from (<see cref="X0"/>, <see cref="Z0"/>) to (<see cref="X1"/>, <see cref="Z1"/>), in game units.
    /// </summary>
    public sealed record MapArea(string Name, double X0, double Z0, double X1, double Z1);

    /// <param name="Markers">The names of the markers, in order.</param>
    public sealed record MapChain(string Name, IReadOnlyList<string> Markers);

    /// <summary>
    /// An army of the save file, with the units it starts with in <see cref="Units"/>.
    /// </summary>
    /// <param name="Alliances">The stance towards other armies by name, e.g. <c>Enemy</c>.</param>
    public sealed record MapArmy(
        string Name,
        string? Personality,
        string? Plans,
        int? Color,
        int? Faction,
        MapEconomy? Economy,
        IReadOnlyDictionary<string, string> Alliances,
        MapUnitGroup Units,
        LuaData.Table Raw);

    /// <summary>
    /// The resources an army starts with.
    /// </summary>
    public sealed record MapEconomy(double Mass, double Energy);

    /// <summary>
    /// A named group of units and further groups (<c>GROUP { ... }</c>). An army's root group is
    /// called <c>Units</c> and usually holds the group <c>INITIAL</c>.
    /// </summary>
    public sealed record MapUnitGroup(string Name, string? Orders, string? Platoon, IReadOnlyList<MapUnitGroup> Groups, IReadOnlyList<MapUnit> Units)
    {
        /// <summary>
        /// The units of this group and of every group in it.
        /// </summary>
        public IEnumerable<MapUnit> AllUnits => Units.Concat(Groups.SelectMany(group => group.AllUnits));
    }

    /// <summary>
    /// A unit placed on the map, e.g. a civilian building or a wreck.
    /// </summary>
    /// <param name="Name">The unit's name in the save file, e.g. <c>UNIT_5</c>.</param>
    /// <param name="BlueprintId">The unit's blueprint id as written, e.g. <c>uel0307</c>.</param>
    /// <param name="Orientation">The rotation around each axis, in radians.</param>
    public sealed record MapUnit(string Name, string BlueprintId, Vector3 Position, Vector3 Orientation, string? Orders, string? Platoon);

    /// <summary>
    /// Reads a map's <c>_save.lua</c>.
    /// </summary>
    public static partial class MapSaveParser
    {
        /// <summary>
        /// Runs the file and reads the <c>Scenario</c> it assigns.
        /// </summary>
        /// <exception cref="LuaSyntaxException">The file is not valid Lua.</exception>
        /// <exception cref="FormatException">The file does not assign <c>Scenario</c>.</exception>
        public static MapSave Parse(string source)
        {
            IReadOnlyDictionary<string, LuaData> globals = LuaSourceParser.Execute(source, MapLuaFunctions.All, Categories(source));
            if (!globals.TryGetValue("Scenario", out LuaData? value) || value is not LuaData.Table scenario)
            {
                throw new FormatException("The save file does not assign Scenario");
            }

            LuaTableReader t = new LuaTableReader(scenario);

            IReadOnlyList<MapMarker> markers = t
                .SectionOrEmpty("MasterChain", chains => chains.SectionOrEmpty("_MASTERCHAIN_", chain => chain.Dictionary("Markers", marker => marker)))
                .Select(marker => new MapMarker(
                    marker.Key,
                    marker.Value.String("type") ?? "",
                    Vector(marker.Value.Numbers("position")),
                    Vector(marker.Value.Numbers("orientation")),
                    marker.Value.Table))
                .ToList();

            IReadOnlyList<MapArea> areas = t.Dictionary("Areas", area => area.Numbers("rectangle"))
                .Where(area => area.Value.Count >= 4)
                .Select(area => new MapArea(area.Key, area.Value[0], area.Value[1], area.Value[2], area.Value[3]))
                .ToList();

            IReadOnlyList<MapChain> chains = t.Dictionary("Chains", chain => chain.Strings("Markers"))
                .Select(chain => new MapChain(chain.Key, chain.Value))
                .ToList();

            IReadOnlyList<MapArmy> armies = t.Dictionary("Armies", army => army)
                .Select(army => ReadArmy(army.Key, army.Value))
                .ToList();

            return new MapSave
            {
                Raw = scenario,
                Markers = markers,
                Areas = areas,
                Chains = chains,
                Armies = armies,
            };
        }

        /// <summary>
        /// The game's <c>categories</c>, which the platoon builders of co-op missions use
        /// (<c>categories.ual0105</c>): each category the file names reads as its name.
        /// </summary>
        private static Dictionary<string, LuaData> Categories(string source)
        {
            Dictionary<string, LuaData> categories = new Dictionary<string, LuaData>();
            foreach (Match match in CategoryPattern().Matches(source))
            {
                string name = match.Groups[1].Value;
                categories[name] = new LuaData.String(name);
            }
            return new Dictionary<string, LuaData> { ["categories"] = new LuaData.Table(categories) };
        }

        [GeneratedRegex(@"\bcategories\.([A-Za-z_][A-Za-z0-9_]*)")]
        private static partial Regex CategoryPattern();

        /// <summary>
        /// A position or orientation: <c>VECTOR3( x, y, z )</c> for markers, <c>{ x, y, z }</c> for units.
        /// </summary>
        private static Vector3 Vector(IReadOnlyList<double> numbers) =>
            numbers.Count >= 3 ? new Vector3((float)numbers[0], (float)numbers[1], (float)numbers[2]) : Vector3.Zero;

        private static MapArmy ReadArmy(string name, LuaTableReader army) => new MapArmy(
            name,
            army.String("personality"),
            army.String("plans"),
            army.Integer("color"),
            army.Integer("faction"),
            army.Section("Economy", economy => new MapEconomy(economy.Number("mass") ?? 0, economy.Number("energy") ?? 0)),
            army.StringDictionary("Alliances"),
            army.SectionOrEmpty("Units", units => ReadGroup("Units", units)),
            army.Table);

        /// <summary>
        /// Reads a group: <c>GROUP</c> tags its table with <c>type = 'GROUP'</c>, a unit's
        /// <c>type</c> is its blueprint id.
        /// </summary>
        private static MapUnitGroup ReadGroup(string name, LuaTableReader group)
        {
            List<MapUnitGroup> groups = new List<MapUnitGroup>();
            List<MapUnit> units = new List<MapUnit>();
            foreach ((string key, LuaTableReader entry) in group.Dictionary("Units", entry => entry))
            {
                string? type = entry.String("type");
                if (type == "GROUP")
                {
                    groups.Add(ReadGroup(key, entry));
                }
                else if (type is not null)
                {
                    units.Add(new MapUnit(
                        key,
                        type,
                        Vector(entry.Numbers("Position")),
                        Vector(entry.Numbers("Orientation")),
                        entry.String("orders"),
                        entry.String("platoon")));
                }
            }

            return new MapUnitGroup(name, group.String("orders"), group.String("platoon"), groups, units);
        }
    }
}
