using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json.Serialization;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Workspaces
{
    /// <summary>
    /// A unit of a units preset: its blueprint, its place relative to the middle of the preset (x
    /// and z in ogrids, y its height above the terrain) and its heading in radians.
    /// </summary>
    public sealed record UnitPresetUnit(string Blueprint, float X, float Y, float Z, float Heading);

    /// <summary>
    /// A group of a units preset, with its units and the groups in it.
    /// </summary>
    public sealed record UnitPresetGroup(string Name, IReadOnlyList<UnitPresetUnit> Units, IReadOnlyList<UnitPresetGroup> Groups);

    /// <summary>
    /// What a units preset holds (user stories U-4, F-10): a group of units to place again, for any
    /// army, and how many units it has.
    /// </summary>
    /// <param name="Role">Whose units they are (<see cref="ArmyRoles"/>: <c>civilian</c>, <c>enemy</c>,
    /// <c>player</c>), which picks the army they go to on the map they are placed on
    /// (<see cref="UnitPresets.ArmyFor"/>); null to place them for the army chosen.</param>
    /// <param name="Wrecks">Whether they are placed as wrecks (user story U-5), into the army's <c>WRECKAGE</c> group.</param>
    /// <param name="Source">Where the units came from, for the reader: a map, its army and group.</param>
    public sealed record UnitPresetSettings(
        UnitPresetGroup Group,
        int Count,
        string? Role = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Wrecks = false,
        string? Source = null);

    /// <summary>
    /// Units presets as the map's model: the group a preset holds, the army its role maps to on a
    /// map, and placing it.
    /// </summary>
    public static class UnitPresets
    {
        public const string Kind = PresetKinds.Units;

        /// <summary>
        /// The group of units a preset holds, to place with <see cref="UnitGroupEdits.PlaceUnits"/>.
        /// </summary>
        public static UnitGroup ToTemplate(UnitPresetGroup group) => new UnitGroup
        {
            Name = group.Name,
            Units = [.. (group.Units ?? []).Select(unit => new Unit
            {
                Name = "",
                Blueprint = unit.Blueprint,
                Position = new Vector3(unit.X, unit.Y, unit.Z),
                Orientation = new Vector3(0, unit.Heading, 0),
            })],
            Groups = [.. (group.Groups ?? []).Select(ToTemplate)],
        };

        /// <summary>
        /// A group of units (<see cref="UnitGroupEdits.CaptureUnits"/>) as a preset's.
        /// </summary>
        public static UnitPresetGroup ToPreset(UnitGroup group) => new UnitPresetGroup(
            group.Name,
            [.. group.Units.Select(unit => new UnitPresetUnit(unit.Blueprint, unit.Position.X, unit.Position.Y, unit.Position.Z, unit.Orientation.Y))],
            [.. group.Groups.Select(ToPreset)]);

        /// <summary>
        /// How many units a preset's group holds, with the groups in it.
        /// </summary>
        public static int CountUnits(UnitPresetGroup group) => (group.Units?.Count ?? 0) + (group.Groups ?? []).Sum(CountUnits);

        /// <summary>
        /// The army of a map a role's units go to when placed at a place, among the armies the map
        /// has (a preset makes none, as selection presets put their units on the map's armies):
        /// <list type="bullet">
        /// <item><c>civilian</c>: the civilians (<see cref="AdaptiveGroups.CivilianArmy"/>: <c>ARMY_17</c>,
        /// else <c>NEUTRAL_CIVILIAN</c>, else the first army no player takes).</item>
        /// <item><c>enemy</c>: an army no player takes besides the civilians (<c>ARMY_9</c> first), else
        /// the civilians, whom the lobby can make hostile.</item>
        /// <item><c>player</c>: the army whose spawn is nearest to the place, else the first playable army.
        /// With the symmetry on, the images go to the armies of the partner spawns.</item>
        /// </list>
        /// </summary>
        /// <returns>The army, or null when the map has none for the role (or the role is no known one).</returns>
        public static string? ArmyFor(MapDocument document, string? role, float x, float z)
        {
            string[] extra = [.. document.Armies.Select(army => army.Name).Where(name => !document.IsPlayable(name))];
            switch (role)
            {
                case ArmyRoles.Civilian:
                    return AdaptiveGroups.CivilianArmy(document);
                case ArmyRoles.Enemy:
                    string? civilians = AdaptiveGroups.CivilianArmy(document);
                    return extra.FirstOrDefault(name => name == "ARMY_9")
                        ?? extra.FirstOrDefault(name => name != civilians && name != "NEUTRAL_CIVILIAN")
                        ?? civilians;
                case ArmyRoles.Player:
                    string[] playable = [.. document.Armies.Select(army => army.Name).Where(document.IsPlayable)];
                    return document.Markers.Spawns
                        .Where(spawn => playable.Contains(spawn.Name))
                        .OrderBy(spawn => Vector2.DistanceSquared(new Vector2(spawn.Position.X, spawn.Position.Z), new Vector2(x, z)))
                        .Select(spawn => spawn.Name)
                        .FirstOrDefault()
                        ?? playable.FirstOrDefault();
                default:
                    return null;
            }
        }

        /// <summary>
        /// Places a preset as a new group named after it, its middle at a place and turned about it,
        /// each unit as high above the terrain as it was saved: for <paramref name="army"/> when
        /// given, else for the army of the preset's role (<see cref="ArmyFor"/>), else for
        /// <paramref name="fallback"/>; into <paramref name="parent"/> (its groups made when missing),
        /// or the army's <c>WRECKAGE</c> group when the preset holds wrecks or <paramref name="wrecks"/> says so.
        /// </summary>
        /// <param name="radians">The turn, clockwise in the top view.</param>
        /// <param name="placed">The units' names.</param>
        /// <param name="group">The new group's path.</param>
        /// <param name="placedFor">The army they were placed for.</param>
        /// <exception cref="ArgumentException">No army to place for, or the map has no such army.</exception>
        public static MapDocument Place(MapDocument document, UnitPresetSettings settings, string name, string? army, string? fallback,
            IReadOnlyList<string> parent, bool? wrecks, float x, float z, float radians,
            out ImmutableArray<string> placed, out ImmutableArray<string> group, out string placedFor)
        {
            placedFor = army ?? ArmyFor(document, settings.Role, x, z) ?? fallback
                ?? throw new ArgumentException("The map has no army to place units for: add one in the Map mode");
            string chosen = placedFor;
            if (!document.Armies.Any(a => a.Name == chosen))
            {
                throw new ArgumentException($"The map has no army {chosen}");
            }
            ImmutableArray<string> into = wrecks ?? settings.Wrecks ? document.WreckagePath(chosen) : [.. parent];
            for (int depth = 1; depth <= into.Length; depth++)
            {
                if (document.FindGroup(chosen, into[..depth]) is null)
                {
                    document = document.AddGroup(chosen, into[..(depth - 1)], into[depth - 1], out _);
                }
            }
            UnitGroup template = ToTemplate(settings.Group) with { Name = name.Replace('/', '-') };
            return document.PlaceUnits(template, chosen, into, x, z, radians, out placed, out group);
        }
    }
}
