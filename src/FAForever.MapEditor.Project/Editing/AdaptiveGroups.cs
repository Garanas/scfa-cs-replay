using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// A group of units an adaptive map's script creates when a lobby option asks for it
    /// (<see cref="AdaptiveGroups"/>).
    /// </summary>
    /// <param name="Group">Its name, e.g. <c>Optional_Civilian_Defenses_3</c>.</param>
    /// <param name="Option">What the option is called in the name, e.g. <c>Civilian_Defenses</c>.</param>
    /// <param name="Level">The option's value from which on it is created, e.g. 3; null for a name without one.</param>
    /// <param name="Units">How many units it holds, with its groups.</param>
    public sealed record OptionalGroup(string Army, string Group, string Option, int? Level, int Units);

    /// <summary>
    /// The groups of civilians and wrecks an adaptive map creates by its lobby options (user story
    /// R-5). The game has no field for this: FAF's adaptive maps (the script by CookieNoob and
    /// KeyBlue that the FAF map editor writes, e.g. <c>adaptive_canis_river</c>, <c>dualgap_adaptive</c>)
    /// keep these units in groups directly under an army's units, beside <c>INITIAL</c> (whose units
    /// the game creates at the start whatever is chosen), named <c>Optional_&lt;option&gt;_&lt;n&gt;</c>:
    /// the script reads the option from the map's <c>_options.lua</c> (<c>optional_civilian_defenses</c>,
    /// <c>optional_wreckage</c>, ...) and calls <c>ScenarioUtils.CreateArmyGroup(army, group, wreck)</c>
    /// for the groups of that value and the values below it (<c>Optional_Wreckage_2</c> up to the
    /// chosen value; a value can also ask for the group as wrecks). Deposits per army are the map's
    /// markers (<see cref="ResourceMarker.SpawnWithArmy"/>); which civilians exist for which number
    /// of players is not part of that convention: the script decides by option, not by player count.
    /// </summary>
    public static partial class AdaptiveGroups
    {
        public const string Prefix = "Optional_";

        [GeneratedRegex(@"^Optional_(.+?)(?:_(\d+))?$")]
        private static partial Regex OptionalName();

        [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_]*$")]
        private static partial Regex OptionName();

        /// <summary>
        /// The optional groups of every army, in the order of the armies and their groups.
        /// </summary>
        public static IReadOnlyList<OptionalGroup> Find(MapDocument document) =>
        [
            .. document.Armies.SelectMany(army => army.Units.Groups
                .Select(group => (group, match: OptionalName().Match(group.Name)))
                .Where(entry => entry.match.Success)
                .Select(entry => new OptionalGroup(
                    army.Name,
                    entry.group.Name,
                    entry.match.Groups[1].Value,
                    entry.match.Groups[2].Success ? int.Parse(entry.match.Groups[2].ValueSpan, CultureInfo.InvariantCulture) : null,
                    UnitGroupEdits.CountUnits(entry.group)))),
        ];

        /// <summary>
        /// The name of the group an option creates from a value on: <c>Optional_Civilian_Base_2</c>.
        /// </summary>
        /// <exception cref="ArgumentException">The option is not a name of letters, digits and underscores, or the level is below 2.</exception>
        public static string GroupName(string option, int? level)
        {
            option = option.Trim();
            if (option.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            {
                option = option[Prefix.Length..];
            }
            if (!OptionName().IsMatch(option))
            {
                throw new ArgumentException($"'{option}' is no option name: letters, digits and underscores, e.g. Civilian_Defenses");
            }
            if (level is < 2)
            {
                throw new ArgumentException("The first value of an option (1) creates nothing: a group starts at 2");
            }
            return level is int n ? $"{Prefix}{option}_{n}" : $"{Prefix}{option}";
        }

        /// <summary>
        /// The army whose groups the adaptive script creates: <c>ARMY_17</c> (the script's), else
        /// <c>NEUTRAL_CIVILIAN</c>, else the first extra army; null without one.
        /// </summary>
        public static string? CivilianArmy(MapDocument document)
        {
            string[] extra = [.. document.Armies.Select(army => army.Name).Where(name => !document.Scenario.PlayableArmies.Contains(name))];
            return extra.FirstOrDefault(name => name == "ARMY_17")
                ?? extra.FirstOrDefault(name => name == "NEUTRAL_CIVILIAN")
                ?? extra.FirstOrDefault();
        }

        /// <summary>
        /// Adds the group an option creates from a value on, directly under the army's units.
        /// </summary>
        /// <exception cref="InvalidOperationException">The army has such a group already.</exception>
        public static MapDocument AddOptionalGroup(this MapDocument document, string army, string option, int? level, out string name)
        {
            name = GroupName(option, level);
            string wanted = name;
            if (document.Armies.FirstOrDefault(a => a.Name == army) is Army found && found.Units.Groups.Any(group => group.Name == wanted))
            {
                throw new InvalidOperationException($"{army} has a group {name} already");
            }
            return document.AddGroup(army, ImmutableArray<string>.Empty, name, out _);
        }
    }
}
