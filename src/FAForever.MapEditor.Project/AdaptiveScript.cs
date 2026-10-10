using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FAForever.FileFormats.Lua;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project
{
    /// <summary>
    /// The files of an adaptive map as FAF's adaptive maps write them (user story R-5): the lobby
    /// options (<c>_options.lua</c>), the tables of which deposits belong to which player
    /// (<c>_tables.lua</c>) and a script that reads both (<c>_script.lua</c>), after the convention of
    /// the adaptive maps by CookieNoob and KeyBlue (<c>adaptive_archsimkats_valley</c>: its script,
    /// options and tables).
    /// </summary>
    /// <remarks>
    /// What the editor holds becomes those files:
    /// <list type="bullet">
    /// <item>Deposits that wait for an army (<see cref="ResourceMarker.SpawnWithArmy"/>) are the tables'
    /// <c>spwnMexArmy</c> and <c>spwnHydroArmy</c>, a row per playable army in the scenario's order;
    /// the option <c>dynamic_spawn</c> decides as in the convention whose deposits spawn: the armies
    /// in the game and their mirror slots (the pairs 1 and 2, 3 and 4, ...), only those in the game,
    /// or only pairs with both slots taken.</item>
    /// <item>The groups <c>Optional_&lt;option&gt;_&lt;n&gt;</c> of each army (<see cref="AdaptiveGroups"/>)
    /// are a lobby option <c>optional_&lt;option&gt;</c> each, from 1 (none) to its highest n: the script
    /// creates the groups of the chosen value and those below it, as wrecks when the option's name
    /// holds <c>Wreck</c> (the convention's <c>Optional_Wreckage_n</c>). A group without a number is
    /// created from value 2 on.</item>
    /// <item>The groups <c>Optional_Players_&lt;n&gt;</c> are no lobby option: the script creates them
    /// when n or more players are in the game, so a map's civilians can grow with the number of
    /// players (the editor's addition to the convention, whose script decides by option only).</item>
    /// </list>
    /// The export writes them for a map that adapts (<see cref="Adapts"/>) whose script is none, the
    /// new map's (<see cref="NewMap.DefaultScript"/>) or one the export wrote before (its first line
    /// is <see cref="Header"/>): a script of the map maker's own is theirs to keep, and so are
    /// options and tables the map's folder holds already.
    /// </remarks>
    public static partial class AdaptiveScript
    {
        /// <summary>
        /// The first line of a script the export writes, by which it knows the script as its own.
        /// </summary>
        public const string Header = "-- Adaptive map script written by the FAF map editor";

        /// <summary>
        /// The option of the groups the script creates by the number of players.
        /// </summary>
        public const string PlayersOption = "Players";

        [GeneratedRegex(@"^(Mass|Hydrocarbon) (.+)$")]
        private static partial Regex DepositName();

        /// <summary>
        /// Whether a map adapts to its players: it says so (<see cref="ScenarioInfo.AdaptiveMap"/>), a
        /// deposit waits for an army, or an army has optional groups.
        /// </summary>
        public static bool Adapts(MapDocument document) =>
            document.Scenario.AdaptiveMap
            || document.Markers.Resources.Any(resource => resource.SpawnWithArmy is not null)
            || AdaptiveGroups.Find(document).Count > 0;

        /// <summary>
        /// Whether the export writes the adaptive files of a map: it adapts, and its script is none,
        /// the new map's or one the export wrote before.
        /// </summary>
        public static bool Writes(MapDocument document) => Adapts(document) && IsReplaceable(document.Script);

        private static bool IsReplaceable(System.Collections.Immutable.ImmutableArray<byte>? script)
        {
            if (script is not { } bytes)
            {
                return true;
            }
            string text = MapImporter.LuaEncoding.GetString(bytes.AsSpan()).ReplaceLineEndings("\n").Trim();
            return text.Length == 0 || text.StartsWith(Header, StringComparison.Ordinal) || text == NewMap.DefaultScript.ReplaceLineEndings("\n").Trim();
        }

        /// <summary>
        /// The lobby options of the optional groups (<c>_options.lua</c>), after <c>dynamic_spawn</c>.
        /// </summary>
        public static string Options(MapDocument document)
        {
            StringBuilder lua = new StringBuilder();
            lua.Append("-- The lobby options of this adaptive map, written by the FAF map editor (user story R-5).\n");
            lua.Append("-- Each option of the optional groups creates the groups Optional_<option>_<n> up to the value chosen.\n\n");
            lua.Append("options = {\n");
            lua.Append("""
                {
                    default = 1,
                    label = "<LOC adaptive_dynamic_spawn_label> Dynamic Spawn Of Resources",
                    help = "<LOC adaptive_dynamic_spawn_help> Determine which mexes & hydros should be spawned.",
                    key = 'dynamic_spawn',
                    pref = 'dynamic_spawn',
                    values = {
                        { text = "<LOC adaptive_dynamic_spawn_key_1_label> mirror slots", help = "<LOC adaptive_dynamic_spawn_key_1_help> Spawn resources for player & mirror slot (balanced resources).", key = 1, },
                        { text = "<LOC adaptive_dynamic_spawn_key_2_label> used slots", help = "<LOC adaptive_dynamic_spawn_key_2_help> Only spawn resources for player on used slots (unbalanced resources).", key = 2, },
                        { text = "<LOC adaptive_dynamic_spawn_key_3_label> no mirror = no resources", help = "<LOC adaptive_dynamic_spawn_key_3_help> Only spawn resources if mirror slot is also occupied by a player (not recommended, but it can make uneven matches fairer).", key = 3, },
                    },
                },

                """.ReplaceLineEndings("\n"));
            foreach (LobbyOption option in LobbyOptions(document))
            {
                lua.Append("{\n");
                lua.Append("    default = 1,\n");
                lua.Append(CultureInfo.InvariantCulture, $"    label = {LuaSourceWriter.Literal(option.Label)},\n");
                lua.Append(CultureInfo.InvariantCulture, $"    help = {LuaSourceWriter.Literal($"How many of the map's {option.Label.ToLowerInvariant()} to create.")},\n");
                lua.Append(CultureInfo.InvariantCulture, $"    key = {LuaSourceWriter.Literal(option.Key)},\n");
                lua.Append(CultureInfo.InvariantCulture, $"    pref = {LuaSourceWriter.Literal(option.Key)},\n");
                lua.Append("    values = {\n");
                lua.Append(CultureInfo.InvariantCulture, $"        {{ text = \"<LOC adaptive_disabled> disabled\", help = {LuaSourceWriter.Literal($"No {option.Label.ToLowerInvariant()}.")}, key = 1, }},\n");
                for (int value = 2; value <= option.Highest; value++)
                {
                    string text = option.Highest == 2 ? "<LOC adaptive_enabled> enabled" : $"level {value - 1}";
                    lua.Append(CultureInfo.InvariantCulture, $"        {{ text = {LuaSourceWriter.Literal(text)}, help = {LuaSourceWriter.Literal($"Create the groups up to Optional_{option.Name}_{value}.")}, key = {value}, }},\n");
                }
                lua.Append("    },\n");
                lua.Append("},\n\n");
            }
            lua.Append("};\n");
            return lua.ToString();
        }

        /// <summary>
        /// The tables of the deposits per player (<c>_tables.lua</c>): the playable armies, and per
        /// army the numbers of its mass and hydrocarbon deposits (what follows <c>Mass </c> in their
        /// names; a deposit named otherwise cannot be named by the convention and always spawns).
        /// </summary>
        public static string Tables(MapDocument document)
        {
            string[] armies = [.. document.Scenario.PlayableArmies];
            string Row(string army, Files.ResourceKind kind) => "{" + string.Join(", ", document.Markers.Resources
                .Where(resource => resource.SpawnWithArmy == army && resource.Kind == kind)
                .Select(resource => DepositName().Match(resource.Name))
                .Where(match => match.Success && match.Groups[1].Value == (kind == Files.ResourceKind.Mass ? "Mass" : "Hydrocarbon"))
                .Select(match => LuaSourceWriter.Literal(match.Groups[2].Value))) + "}";

            StringBuilder lua = new StringBuilder();
            lua.Append("-- Which deposits belong to which player, written by the FAF map editor (user story R-5):\n");
            lua.Append("-- a row per playable army, in the order of playableArmies.\n\n");
            lua.Append(CultureInfo.InvariantCulture, $"maxPlayerOnMap = {armies.Length}\n\n");
            lua.Append("playableArmies = {" + string.Join(", ", armies.Select(LuaSourceWriter.Literal)) + "}\n\n");
            lua.Append("spwnMexArmy = {\n");
            foreach (string army in armies)
            {
                lua.Append(CultureInfo.InvariantCulture, $"    {Row(army, Files.ResourceKind.Mass)}, -- {army}\n");
            }
            lua.Append("}\n\n");
            lua.Append("spwnHydroArmy = {\n");
            foreach (string army in armies)
            {
                lua.Append(CultureInfo.InvariantCulture, $"    {Row(army, Files.ResourceKind.Hydrocarbon)}, -- {army}\n");
            }
            lua.Append("}\n");
            return lua.ToString();
        }

        /// <summary>
        /// The script (<c>_script.lua</c>): the armies at the start, the optional groups by the lobby's
        /// options and by the number of players, the deposits by who is in the game.
        /// </summary>
        /// <param name="tables">The game's path of the tables, <c>/maps/&lt;folder&gt;/&lt;file&gt;_tables.lua</c>.</param>
        public static string Script(MapDocument document, string tables)
        {
            LobbyOption[] options = [.. LobbyOptions(document)];
            IReadOnlyList<OptionalGroup> groups = AdaptiveGroups.Find(document);
            StringBuilder lua = new StringBuilder();
            lua.Append(Header).Append('\n');
            lua.Append("-- after the adaptive maps' script by CookieNoob and KeyBlue: the lobby's options (_options.lua) and\n");
            lua.Append("-- the deposits per player (_tables.lua) decide what the map holds. Written again with every export.\n");
            lua.Append("local ScenarioUtils = import('/lua/sim/ScenarioUtilities.lua')\n");
            lua.Append("local ScenarioFramework = import('/lua/ScenarioFramework.lua')\n");
            lua.Append(CultureInfo.InvariantCulture, $"local Tables = import({LuaSourceWriter.Literal(tables)})\n\n");
            lua.Append("local maxPlayerOnMap = Tables.maxPlayerOnMap or 16\n");
            lua.Append("local playableArmies = Tables.playableArmies or {}\n");
            lua.Append("local dynamic_spawn = ScenarioInfo.Options.dynamic_spawn or 1\n");
            foreach (LobbyOption option in options)
            {
                lua.Append(CultureInfo.InvariantCulture, $"local {option.Key} = ScenarioInfo.Options.{option.Key} or 1\n");
            }
            lua.Append("""

                function OnPopulate()
                    ScenarioUtils.InitializeArmies()
                    OptionalUnits()
                end

                function OnStart(self)
                end

                -- whether an army is in the game
                function IsInGame(name)
                    for _, army in ipairs(ListArmies()) do
                        if army == name then
                            return true
                        end
                    end
                    return false
                end

                -- how many of the playable armies are in the game
                function PlayerCount()
                    local count = 0
                    for _, name in ipairs(playableArmies) do
                        if IsInGame(name) then
                            count = count + 1
                        end
                    end
                    return count
                end

                -- the groups of the lobby's options up to the value chosen, and those of the number of players
                function OptionalUnits()
                    local players = PlayerCount()

                """.ReplaceLineEndings("\n"));
            foreach (OptionalGroup group in groups)
            {
                bool wreck = group.Option.Contains("Wreck", StringComparison.OrdinalIgnoreCase);
                string create = $"ScenarioUtils.CreateArmyGroup({LuaSourceWriter.Literal(group.Army)}, {LuaSourceWriter.Literal(group.Group)}, {(wreck ? "true" : "false")})";
                string condition = group.Option == PlayersOption
                    ? $"players >= {group.Level ?? 1}"
                    : $"{KeyOf(group.Option)} >= {group.Level ?? 2}";
                lua.Append(CultureInfo.InvariantCulture, $"    if {condition} then\n        {create}\n    end\n");
            }
            lua.Append("""
                end

                -- the deposits: those of armies not in the game (by the option dynamic_spawn) do not spawn
                function ScenarioUtils.CreateResources()
                    local markers = ScenarioUtils.GetMarkers()
                    local spwnMexArmy = Tables.spwnMexArmy or {}
                    local spwnHydroArmy = Tables.spwnHydroArmy or {}
                    local absent = {}
                    for index, name in ipairs(playableArmies) do
                        -- the mirror slots are the pairs 1 and 2, 3 and 4, ...
                        local odd = index - math.floor(index / 2) * 2 == 1
                        local mirror = playableArmies[odd and index + 1 or index - 1]
                        local here = IsInGame(name)
                        local mirrorHere = mirror ~= nil and IsInGame(mirror)
                        if dynamic_spawn == 1 then
                            absent[index] = not here and not mirrorHere
                        elseif dynamic_spawn == 2 then
                            absent[index] = not here
                        else
                            absent[index] = not (here and mirrorHere)
                        end
                    end
                    for name, marker in pairs(markers) do
                        if marker.resource then
                            local spawn = true
                            for index, isAbsent in pairs(absent) do
                                if isAbsent then
                                    spawn = spawn and not InList(name, spwnMexArmy[index], 'Mass ')
                                    spawn = spawn and not InList(name, spwnHydroArmy[index], 'Hydrocarbon ')
                                end
                            end
                            if spawn then
                                SpawnResource(marker.position, marker.type)
                            end
                        end
                    end
                end

                function InList(name, list, prefix)
                    for _, number in ipairs(list or {}) do
                        if name == prefix .. number then
                            return true
                        end
                    end
                    return false
                end

                function SpawnResource(position, kind)
                    local bp, albedo, size, lod
                    if kind == 'Mass' then
                        albedo = '/env/common/splats/mass_marker.dds'
                        bp = '/env/common/props/massDeposit01_prop.bp'
                        size = 2
                        lod = 100
                    else
                        albedo = '/env/common/splats/hydrocarbon_marker.dds'
                        bp = '/env/common/props/hydrocarbonDeposit01_prop.bp'
                        size = 6
                        lod = 200
                    end
                    CreateResourceDeposit(kind, position[1], position[2], position[3], size / 2)
                    CreatePropHPR(bp, position[1], position[2], position[3], Random(0, 360), 0, 0)
                    CreateSplat(position, 0, albedo, size, size, lod, 0, -1, 0)
                end

                """.ReplaceLineEndings("\n"));
            return lua.ToString();
        }

        /// <summary>
        /// A lobby option of the optional groups: its name in the groups' names, its key, its label and
        /// the highest value its groups ask for (at least 2).
        /// </summary>
        public sealed record LobbyOption(string Name, string Key, string Label, int Highest);

        /// <summary>
        /// The lobby options the map's optional groups ask for, by name; the groups of the number of
        /// players are none.
        /// </summary>
        public static IEnumerable<LobbyOption> LobbyOptions(MapDocument document) => AdaptiveGroups.Find(document)
            .Where(group => group.Option != PlayersOption)
            .GroupBy(group => group.Option, StringComparer.Ordinal)
            .OrderBy(option => option.Key, StringComparer.Ordinal)
            .Select(option => new LobbyOption(option.Key, KeyOf(option.Key), option.Key.Replace('_', ' '), Math.Max(2, option.Max(group => group.Level ?? 2))));

        /// <summary>
        /// The lobby option of an option's name: <c>Civilian_Base</c> is <c>optional_civilian_base</c>.
        /// </summary>
        public static string KeyOf(string option) => "optional_" + option.ToLowerInvariant();
    }
}
