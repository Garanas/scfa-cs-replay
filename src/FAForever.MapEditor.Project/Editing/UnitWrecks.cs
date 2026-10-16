using System.Collections.Immutable;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// Units placed as wrecks (user story U-5). The game has no field for it: a map's
    /// <c>_save.lua</c> holds wrecks as ordinary units in a group named exactly <c>WRECKAGE</c> of an
    /// army, and the game's <c>ScenarioUtils.InitializeArmies</c> (<c>lua/sim/ScenarioUtilities.lua</c>)
    /// creates that group's units at the start and turns each into its wreck
    /// (<c>CreateWreckageUnit</c>: the unit's wreckage prop, then the unit is destroyed). It looks the
    /// group up by name at any depth of the army's tree (<c>FindUnitGroup</c>, case-sensitive, the
    /// first one it meets), also with the civilians turned off. 34 maps of the vault keep wrecks
    /// this way, nearly always in an extra army (<c>ARMY_9</c>, <c>NEUTRAL_CIVILIAN</c>, <c>ARMY_17</c>)
    /// with the default script. So the editor keeps a wreck as a unit in that group, and the
    /// import and export carry it unchanged.
    /// </summary>
    /// <remarks>
    /// Adaptive maps create other groups as wrecks from their script by lobby option
    /// (<c>ScenarioUtils.CreateArmyGroup(army, 'Optional_Wreckage_2', true)</c>, see
    /// <see cref="AdaptiveGroups"/>); those are ordinary groups to the editor, since only the script
    /// decides. A <c>WRECKAGE</c> group in a playable army is created only when a player takes that slot.
    /// </remarks>
    public static class UnitWrecks
    {
        /// <summary>
        /// The name of the group the game turns into wrecks.
        /// </summary>
        public const string GroupName = "WRECKAGE";

        /// <summary>
        /// Whether a unit lies in a <c>WRECKAGE</c> group, at any depth.
        /// </summary>
        public static bool IsWreck(PlacedUnit unit) => unit.Group.Contains(GroupName, StringComparer.Ordinal);

        /// <summary>
        /// The names of the units that are wrecks.
        /// </summary>
        public static ImmutableHashSet<string> Wrecks(this MapDocument document) =>
            [.. document.AllUnits().Where(IsWreck).Select(unit => unit.Unit.Name)];

        /// <summary>
        /// The path of an army's <c>WRECKAGE</c> group as the game finds it (depth first), or the one a
        /// wreck goes into when the army has none: <c>WRECKAGE</c> under the army's root group.
        /// </summary>
        public static ImmutableArray<string> WreckagePath(this MapDocument document, string army)
        {
            UnitGroup? root = document.FindArmy(army)?.Units;
            return root is not null && Find(root, []) is ImmutableArray<string> found ? found : [GroupName];

            static ImmutableArray<string>? Find(UnitGroup group, ImmutableArray<string> path)
            {
                foreach (UnitGroup child in group.Groups)
                {
                    ImmutableArray<string> childPath = path.Add(child.Name);
                    if (child.Name == GroupName)
                    {
                        return childPath;
                    }
                    if (Find(child, childPath) is ImmutableArray<string> deeper)
                    {
                        return deeper;
                    }
                }
                return null;
            }
        }

        /// <summary>
        /// Makes units wrecks, each into its own army's <c>WRECKAGE</c> group (made when missing), or
        /// back into units, into its army's <c>INITIAL</c>. A unit that is so already stays.
        /// </summary>
        /// <param name="count">How many units changed.</param>
        public static MapDocument SetWrecks(this MapDocument document, IEnumerable<string> names, bool wreck, out int count)
        {
            count = 0;
            foreach (string name in names.Distinct(StringComparer.Ordinal).ToArray())
            {
                if (document.FindUnit(name) is not PlacedUnit unit || IsWreck(unit) == wreck)
                {
                    continue;
                }
                ImmutableArray<string> group = wreck ? document.WreckagePath(unit.Army) : [UnitGroupEdits.InitialGroup];
                document = document.MoveUnits([name], unit.Army, group, out int moved);
                count += moved;
            }
            return document;
        }

        /// <summary>
        /// The armies with wrecks that are playable: their wrecks show only when a player takes the slot.
        /// </summary>
        public static IEnumerable<string> PlayableArmiesWithWrecks(this MapDocument document) =>
            document.AllUnits().Where(IsWreck).Select(unit => unit.Army).Distinct(StringComparer.Ordinal).Where(document.IsPlayable);
    }
}
