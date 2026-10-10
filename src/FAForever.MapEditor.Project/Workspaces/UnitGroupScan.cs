using System.Numerics;
using System.Text.RegularExpressions;
using FAForever.FileFormats.Blueprints;
using FAForever.FileFormats.Map;

namespace FAForever.MapEditor.Project.Workspaces
{
    /// <summary>
    /// The army roles a units preset can name (<see cref="UnitPresetSettings.Role"/>): who its units
    /// belong to on the maps it came from, and so which army of another map they go to.
    /// </summary>
    public static class ArmyRoles
    {
        /// <summary>
        /// Neutral armies: civilian towns, their walls and wrecks (<c>NEUTRAL_CIVILIAN</c>, <c>ARMY_17</c>).
        /// </summary>
        public const string Civilian = "civilian";

        /// <summary>
        /// Armies no player takes that fight: a campaign's AI bases and defences.
        /// </summary>
        public const string Enemy = "enemy";

        /// <summary>
        /// The armies players take.
        /// </summary>
        public const string Player = "player";

        public static IReadOnlyList<string> All { get; } = [Civilian, Enemy, Player];
    }

    /// <summary>
    /// What kind of group a unit group of a map is, from its name and the groups in it
    /// (<see cref="UnitGroupScan.CategoryOf"/>).
    /// </summary>
    public enum UnitGroupCategory
    {
        /// <summary>A base: a group holding at least two role groups (<c>Defense</c>, <c>Factories</c>, ...).</summary>
        Base,
        /// <summary>One role of a base (<c>Defense</c>, <c>Factories</c>, <c>Energy</c>, <c>Mass</c>, <c>Engineers</c>, <c>Misc</c>, ...).</summary>
        BaseRole,
        /// <summary>A defence point: point defences, anti-air, shields, torpedo launchers.</summary>
        Defence,
        Walls,
        /// <summary>Wrecks (<c>WRECKAGE</c>, <c>M2_Wreckage</c>).</summary>
        Wreckage,
        /// <summary>A civilian town or its buildings.</summary>
        Civilian,
        /// <summary>What an army starts with: <c>INITIAL</c>, a commander.</summary>
        Start,
        /// <summary>Patrols, attack waves, platoons, cinematics: mission scripting more than layout.</summary>
        Scripting,
        Other,
    }

    /// <summary>
    /// A group of units of a scanned map, with what its subtree holds.
    /// </summary>
    /// <param name="Path">Its path below the army's root group, joined with <c>/</c>.</param>
    /// <param name="Pattern">Its name with the numbers stripped (<see cref="UnitGroupScan.PatternOf"/>).</param>
    /// <param name="Level">The difficulty a campaign base's group is for (<c>_D1</c> to <c>_D3</c> at the end of its name), else null.</param>
    /// <param name="Units">How many units its subtree holds.</param>
    /// <param name="Direct">How many units it holds itself.</param>
    /// <param name="Faction">The faction most of its units are of (<c>UEF</c>, <c>Aeon</c>, <c>Cybran</c>,
    /// <c>Seraphim</c>, <c>Civilian</c>), <c>Mixed</c> when none has three in five, or <c>Other</c>.</param>
    /// <param name="Width">The extent of its units along x, in ogrids.</param>
    /// <param name="Depth">The extent along z.</param>
    /// <param name="Roles">For a base, the units of each role group in its subtree, by role.</param>
    /// <param name="Blueprints">Its units by blueprint id, the most first.</param>
    public sealed record ScannedGroup(
        string Map,
        string Army,
        string Role,
        string Path,
        string Name,
        string Pattern,
        UnitGroupCategory Category,
        int? Level,
        int Units,
        int Direct,
        string Faction,
        float Width,
        float Depth,
        float X,
        float Z,
        IReadOnlyDictionary<string, int>? Roles,
        IReadOnlyDictionary<string, int> Blueprints);

    /// <summary>
    /// An army of a scanned map and the role it plays there (<see cref="ArmyRoles"/>).
    /// </summary>
    public sealed record ScannedArmy(string Name, string Role, int Units);

    /// <summary>
    /// A scanned map: where it is and what it holds, or why it could not be read.
    /// </summary>
    public sealed record ScannedMap(string Name, string Folder, IReadOnlyList<ScannedArmy> Armies, int Groups, int Units, string? Error);

    /// <summary>
    /// The groups of one name pattern over every scanned map.
    /// </summary>
    /// <param name="MedianUnits">The median of the groups' unit counts.</param>
    /// <param name="MedianSize">The median of the larger of each group's width and depth, in ogrids.</param>
    /// <param name="TopBlueprints">The blueprints met most, with how many units.</param>
    public sealed record PatternSummary(
        string Pattern,
        UnitGroupCategory Category,
        int Groups,
        int Maps,
        int Units,
        IReadOnlyDictionary<string, int> Factions,
        IReadOnlyDictionary<string, int> Roles,
        double MedianUnits,
        double MedianSize,
        IReadOnlyDictionary<string, int> TopBlueprints);

    /// <summary>
    /// What a scan found (<see cref="UnitGroupScan.Scan"/>): the maps, every group with units, and
    /// the patterns of their names.
    /// </summary>
    public sealed record UnitScanResult(IReadOnlyList<string> Folders, IReadOnlyList<ScannedMap> Maps, IReadOnlyList<PatternSummary> Patterns, IReadOnlyList<ScannedGroup> Groups);

    /// <summary>
    /// Scans the unit groups of maps (the <c>_save.lua</c> of each map folder) for the patterns that
    /// make good unit presets: a campaign base split by role, defence points, walls, wrecks,
    /// civilian towns, the units an army starts with. <c>faf-map dev scan-units</c> runs it; the
    /// example unit presets (<see cref="ExampleUnitPresets"/>) were chosen from its report and
    /// made with <see cref="UnitTemplates"/>.
    /// </summary>
    public static partial class UnitGroupScan
    {
        /// <summary>
        /// The names of the role groups of a campaign base, as its base manager names them, lower case.
        /// </summary>
        public static IReadOnlySet<string> RoleNames { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "defense", "defenses", "defence", "defences", "factories", "factory", "production", "energy", "mass",
            "engineers", "misc", "shields", "turrets", "towers", "aa", "torpedo", "torpedoes", "artillery", "walls",
            "intel", "radar", "hydro", "storage",
        };

        /// <summary>
        /// Every map folder under each folder (a folder holding a <c>*_save.lua</c>, the folder itself
        /// or one level down), scanned. A map that cannot be read is listed with its error.
        /// </summary>
        public static UnitScanResult Scan(IEnumerable<string> folders, int topBlueprints = 8)
        {
            string[] roots = [.. folders.Select(Path.GetFullPath)];
            List<ScannedMap> maps = [];
            List<ScannedGroup> groups = [];
            foreach (string folder in roots.SelectMany(MapFolders).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string name = Path.GetFileName(folder);
                try
                {
                    (MapSave save, MapScenario? scenario) = Read(folder);
                    List<ScannedArmy> armies = [];
                    int before = groups.Count;
                    foreach (MapArmy army in save.Armies)
                    {
                        string role = RoleOf(army, scenario);
                        armies.Add(new ScannedArmy(army.Name, role, army.Units.AllUnits.Count()));
                        foreach (MapUnitGroup child in army.Units.Groups)
                        {
                            Collect(groups, name, army.Name, role, child, "");
                        }
                    }
                    maps.Add(new ScannedMap(name, folder, armies, groups.Count - before, armies.Sum(a => a.Units), null));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException or InvalidOperationException or ArgumentException or KeyNotFoundException or InvalidCastException)
                {
                    maps.Add(new ScannedMap(name, folder, [], 0, 0, exception.Message));
                }
            }
            return new UnitScanResult(roots, maps, Summarize(groups, topBlueprints), groups);
        }

        /// <summary>
        /// Writes what a scan found as JSON (camel case, the categories as text).
        /// </summary>
        public static void Write(UnitScanResult result, string file) => ProjectJson.Write(file, result);

        /// <summary>
        /// The map folders in a folder: itself when it holds a <c>*_save.lua</c>, else its sub-folders that do.
        /// </summary>
        public static IEnumerable<string> MapFolders(string folder)
        {
            if (!Directory.Exists(folder))
            {
                return [];
            }
            if (SaveFileOf(folder) is not null)
            {
                return [folder];
            }
            return Directory.EnumerateDirectories(folder).Where(sub => SaveFileOf(sub) is not null).Order(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// A map folder's save and scenario: the <c>*_save.lua</c> named after the folder when there
        /// is one, else the first; the scenario beside it, null when there is none.
        /// </summary>
        /// <exception cref="IOException">The folder has no save file.</exception>
        public static (MapSave Save, MapScenario? Scenario) Read(string folder)
        {
            string save = SaveFileOf(folder) ?? throw new IOException($"{folder} has no _save.lua");
            string scenarioFile = save[..^"_save.lua".Length] + "_scenario.lua";
            MapScenario? scenario = File.Exists(scenarioFile) ? MapScenarioParser.Parse(File.ReadAllText(scenarioFile)) : null;
            // a few campaign saves (SCCA_A04, SCCA_R04) give orders as one-line Lua functions, which
            // the data-only parser refuses; the scan needs none of them
            string text = InlineFunction().Replace(File.ReadAllText(save), "nil");
            return (MapSaveParser.Parse(text), scenario);
        }

        private static string? SaveFileOf(string folder)
        {
            string[] saves = Directory.GetFiles(folder, "*_save.lua");
            string named = Path.Combine(folder, Path.GetFileName(folder.TrimEnd('/', '\\')) + "_save.lua");
            return saves.FirstOrDefault(file => string.Equals(file, named, StringComparison.OrdinalIgnoreCase)) ?? saves.Order(StringComparer.Ordinal).FirstOrDefault();
        }

        /// <summary>
        /// The role an army plays on its map: a player's (named <c>Player</c>, <c>Coop</c> or
        /// <c>ARMY_n</c> among the scenario's teams), the civilians' (named so, or <c>ARMY_17</c>, or
        /// three in five of its units civilian), else an enemy's.
        /// </summary>
        public static string RoleOf(MapArmy army, MapScenario? scenario)
        {
            string name = army.Name;
            bool playable = scenario?.PlayerArmies.Contains(name) ?? name.StartsWith("ARMY_", StringComparison.OrdinalIgnoreCase);
            if (name.StartsWith("Player", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Coop", StringComparison.OrdinalIgnoreCase)
                || (playable && ArmyNumber().IsMatch(name) && name != "ARMY_17"))
            {
                return ArmyRoles.Player;
            }
            if (name.Contains("civilian", StringComparison.OrdinalIgnoreCase) || name.Contains("neutral", StringComparison.OrdinalIgnoreCase) || name == "ARMY_17")
            {
                return ArmyRoles.Civilian;
            }
            MapUnit[] units = [.. army.Units.AllUnits];
            return units.Length > 0 && units.Count(unit => IsCivilianUnit(unit.BlueprintId)) * 5 >= units.Length * 3 ? ArmyRoles.Civilian : ArmyRoles.Enemy;
        }

        /// <summary>
        /// Whether a blueprint is a civilian's: its third letter <c>c</c> (<c>uec1101</c>, <c>xsc9002</c>).
        /// </summary>
        public static bool IsCivilianUnit(string blueprint) => blueprint.Length >= 3 && char.ToLowerInvariant(blueprint[2]) == 'c';

        /// <summary>
        /// The faction of a blueprint as a scan names it: <c>Civilian</c> for a civilian's, else its
        /// faction (<see cref="BlueprintIds.GetFaction"/>), else <c>Other</c>.
        /// </summary>
        public static string FactionOf(string blueprint) =>
            IsCivilianUnit(blueprint) ? "Civilian" : BlueprintIds.GetFaction(blueprint)?.DisplayName() ?? "Other";

        /// <summary>
        /// A group's name with its numbers stripped and the underscores left around them joined:
        /// <c>M1_Walls</c> is <c>M_Walls</c>, <c>GROUP_776</c> is <c>GROUP</c>.
        /// </summary>
        public static string PatternOf(string name)
        {
            string stripped = Regex.Replace(Digits().Replace(name, ""), "_+", "_").Trim('_');
            return stripped.Length == 0 ? "(numbers)" : stripped;
        }

        /// <summary>
        /// What kind of group a name is, given whether the group holds two or more role groups.
        /// </summary>
        public static UnitGroupCategory CategoryOf(string name, bool holdsRoles)
        {
            if (holdsRoles)
            {
                return UnitGroupCategory.Base;
            }
            string bare = PatternOf(name);
            string lower = name.ToLowerInvariant();
            if (lower.Contains("wreck"))
            {
                return UnitGroupCategory.Wreckage;
            }
            if (lower.Contains("wall"))
            {
                return UnitGroupCategory.Walls;
            }
            if (RoleNames.Contains(bare))
            {
                return UnitGroupCategory.BaseRole;
            }
            if (lower is "initial" or "starting_units" or "initial_units" or "commander" or "acu" or "acus" || lower.Contains("commander") || lower.StartsWith("initial", StringComparison.Ordinal) || lower.StartsWith("start", StringComparison.Ordinal))
            {
                return UnitGroupCategory.Start;
            }
            if (Scripting().IsMatch(lower))
            {
                return UnitGroupCategory.Scripting;
            }
            if (CivilianWords().IsMatch(lower))
            {
                return UnitGroupCategory.Civilian;
            }
            if (DefenceWords().IsMatch(lower))
            {
                return UnitGroupCategory.Defence;
            }
            return UnitGroupCategory.Other;
        }

        /// <summary>
        /// The difficulty a group's name ends with (<c>M3_North_Base_Main_D2</c> is 2), else null.
        /// </summary>
        public static int? LevelOf(string name) =>
            Difficulty().Match(name) is { Success: true } match ? int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : null;

        /// <summary>
        /// Whether a group holds two or more role groups, itself or in the groups of the next
        /// difficulty inside it (a campaign base's <c>_D3</c> holds <c>_D2</c>, which holds the roles).
        /// </summary>
        public static bool HoldsRoles(MapUnitGroup group) =>
            group.Groups.Count(child => RoleNames.Contains(PatternOf(child.Name))) >= 2
            || group.Groups.Any(child => LevelOf(child.Name) is not null && HoldsRoles(child));

        private static void Collect(List<ScannedGroup> groups, string map, string army, string role, MapUnitGroup group, string parentPath)
        {
            string path = parentPath.Length == 0 ? group.Name : $"{parentPath}/{group.Name}";
            MapUnit[] units = [.. group.AllUnits];
            if (units.Length > 0)
            {
                Vector2 min = new Vector2(units.Min(u => u.Position.X), units.Min(u => u.Position.Z));
                Vector2 max = new Vector2(units.Max(u => u.Position.X), units.Max(u => u.Position.Z));
                bool holdsRoles = HoldsRoles(group) || LooksLikeBase(units, MathF.Max(max.X - min.X, max.Y - min.Y));
                Dictionary<string, int>? roles = null;
                if (holdsRoles)
                {
                    roles = [];
                    CountRoles(group, roles);
                }
                groups.Add(new ScannedGroup(
                    map, army, role, path, group.Name, PatternOf(group.Name), CategoryOf(group.Name, holdsRoles), LevelOf(group.Name),
                    units.Length, group.Units.Count, MajorityFaction(units.Select(u => u.BlueprintId)),
                    max.X - min.X, max.Y - min.Y, (min.X + max.X) / 2, (min.Y + max.Y) / 2,
                    roles,
                    Count(units.Select(u => u.BlueprintId.ToLowerInvariant()), int.MaxValue)));
            }
            foreach (MapUnitGroup child in group.Groups)
            {
                Collect(groups, map, army, role, child, path);
            }
        }

        /// <summary>
        /// Whether units look like a base without role groups (as some coop maps keep them): eight or
        /// more, seven in ten structures, with a factory and an economy building, within 160 ogrids.
        /// </summary>
        public static bool LooksLikeBase(IReadOnlyCollection<MapUnit> units, float extent)
        {
            if (units.Count < 8 || extent > 160)
            {
                return false;
            }
            string[] structures = [.. units.Select(u => u.BlueprintId.ToLowerInvariant()).Where(id => id.Length >= 7 && id[2] == 'b' && !IsCivilianUnit(id))];
            return structures.Length * 10 >= units.Count * 7
                && structures.Any(id => id[3] == '0')
                && structures.Any(id => id[3] == '1');
        }

        private static void CountRoles(MapUnitGroup group, Dictionary<string, int> roles)
        {
            foreach (MapUnitGroup child in group.Groups)
            {
                string bare = PatternOf(child.Name);
                if (RoleNames.Contains(bare))
                {
                    string role = UnitTemplates.CanonicalRole(bare);
                    roles[role] = roles.GetValueOrDefault(role) + child.AllUnits.Count();
                }
                else
                {
                    CountRoles(child, roles);
                }
            }
        }

        /// <summary>
        /// The faction three in five of the blueprints are of, else <c>Mixed</c>.
        /// </summary>
        public static string MajorityFaction(IEnumerable<string> blueprints)
        {
            string[] factions = [.. blueprints.Select(FactionOf)];
            if (factions.Length == 0)
            {
                return "Other";
            }
            (string faction, int count) = factions.GroupBy(f => f).Select(g => (g.Key, g.Count())).OrderByDescending(g => g.Item2).First();
            return count * 5 >= factions.Length * 3 ? faction : "Mixed";
        }

        private static IReadOnlyList<PatternSummary> Summarize(IReadOnlyList<ScannedGroup> groups, int top) =>
            [.. groups
                .GroupBy(group => (group.Pattern, group.Category))
                .Select(pattern => new PatternSummary(
                    pattern.Key.Pattern,
                    pattern.Key.Category,
                    pattern.Count(),
                    pattern.Select(group => group.Map).Distinct().Count(),
                    pattern.Sum(group => group.Units),
                    Count(pattern.Select(group => group.Faction), int.MaxValue),
                    Count(pattern.Select(group => group.Role), int.MaxValue),
                    Median(pattern.Select(group => (double)group.Units)),
                    Median(pattern.Select(group => (double)MathF.Max(group.Width, group.Depth))),
                    Sum(pattern.SelectMany(group => group.Blueprints), top)))
                .OrderByDescending(pattern => pattern.Groups)
                .ThenBy(pattern => pattern.Pattern, StringComparer.Ordinal)];

        private static IReadOnlyDictionary<string, int> Count(IEnumerable<string> values, int top) =>
            values.GroupBy(value => value, StringComparer.Ordinal)
                .OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.Ordinal)
                .Take(top)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        private static IReadOnlyDictionary<string, int> Sum(IEnumerable<KeyValuePair<string, int>> counts, int top) =>
            counts.GroupBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(group => (group.Key, Total: group.Sum(pair => pair.Value)))
                .OrderByDescending(pair => pair.Total).ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .Take(top)
                .ToDictionary(pair => pair.Key, pair => pair.Total, StringComparer.Ordinal);

        private static double Median(IEnumerable<double> values)
        {
            double[] sorted = [.. values.Order()];
            return sorted.Length == 0 ? 0 : sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
        }

        [GeneratedRegex(@"\bfunction\s*\([^)\n]*\)[^\n]*?\bend\b")]
        private static partial Regex InlineFunction();

        [GeneratedRegex(@"\d+")]
        private static partial Regex Digits();

        [GeneratedRegex(@"^ARMY_\d+$", RegexOptions.IgnoreCase)]
        private static partial Regex ArmyNumber();

        [GeneratedRegex(@"_D([1-3])$", RegexOptions.IgnoreCase)]
        private static partial Regex Difficulty();

        [GeneratedRegex(@"patrol|attack|wave|platoon|adapt|nis|reinforce|transport|hunt|raid|drop|counter|escort|guard|ambush|cinematic|intro|_ai|kill|assault|strike|bomber|gunship|naval|fleet|air_|_air|land_|_land|hover|convoy|truck")]
        private static partial Regex Scripting();

        [GeneratedRegex(@"civ|city|town|village|building|colony|settlement|houses|farm|outpost")]
        private static partial Regex CivilianWords();

        [GeneratedRegex(@"defen[cs]e|^aa|_aa|torpedo|shield|tower|turret|pd|artillery|gun|point")]
        private static partial Regex DefenceWords();
    }
}
